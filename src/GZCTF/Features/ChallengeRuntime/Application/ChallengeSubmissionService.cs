using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.ChallengeRuntime.Domain;
using GZCTF.Features.Dashboard.Domain;
using GZCTF.Features.Dashboard.Application;
using GZCTF.Features.LearningProgress.Domain;
using GZCTF.Features.Leaderboard.Application;
using GZCTF.Features.Leaderboard.Domain;
using GZCTF.Features.QqBot.Application;
using GZCTF.Models;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using CanonicalChallenge = GZCTF.Features.ChallengeLibrary.Domain.Challenge;

namespace GZCTF.Features.ChallengeRuntime.Application;

public sealed record ChallengeSubmissionResult(
    bool Accepted,
    bool FirstSolve,
    ChallengeSolveMode? SolveMode,
    string? RejectionCode,
    Guid SubmissionId);

public sealed class ChallengeSubmissionService(
    AppDbContext db,
    ChallengeRuntimeService runtime,
    DynamicAttachmentAllocator attachments,
    DailySolveProjection dailyProjection,
    DashboardDeltaPublisher dashboardDeltas,
    QqBotNotifier qqBot,
    ILogger<ChallengeSubmissionService> logger,
    Microsoft.Extensions.Caching.Memory.IMemoryCache? rankingCache = null)
{
    public async Task<ChallengeSubmissionResult> SubmitAsync(
        Guid userId, Guid challengeId, string submittedFlag, CancellationToken token = default)
    {
        if (await db.Challenges.AsNoTracking().AnyAsync(c => c.Id == challengeId &&
                c.PublicationState == ChallengePublicationState.Merged, token))
            return new ChallengeSubmissionResult(false, false, null, "challenge.merged", Guid.Empty);
        var challenge = await PublishedCourseContentReader.GetChallengeAsync(db, challengeId, token)
            ?? throw new InvalidOperationException("challenge.not_published");
        IDbContextTransaction? limitTransaction = null;
        try
        {
            if (challenge.SubmissionLimit > 0)
            {
                limitTransaction = await db.Database.BeginTransactionAsync(token);
                var lockInput = userId.ToByteArray().Concat(challengeId.ToByteArray()).ToArray();
                var lockKey = BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(lockInput));
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock({lockKey})", token);
                if (await db.ChallengeSubmissions.CountAsync(item =>
                        item.UserId == userId && item.ChallengeId == challengeId, token) >= challenge.SubmissionLimit)
                {
                    await limitTransaction.CommitAsync(token);
                    return new ChallengeSubmissionResult(false, false, null,
                        "challenge.submission_limit_exhausted", Guid.Empty);
                }
            }

            var instance = await runtime.GetOrCreateInstanceAsync(userId, challengeId, token);
            if (instance.RuntimeMetadataJson is { } metadata)
            {
                using var runtimeMetadata = System.Text.Json.JsonDocument.Parse(metadata);
                if (runtimeMetadata.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    runtimeMetadata.RootElement.TryGetProperty("adminTest", out var adminTest) && adminTest.ValueKind == System.Text.Json.JsonValueKind.True)
                    return new ChallengeSubmissionResult(false, false, null, "challenge.test_instance", Guid.Empty);
            }
            var hasStartedResource = instance.Status == ChallengeInstanceStatus.Running ||
                !string.IsNullOrWhiteSpace(instance.AssignedAttachmentKey);
            var runtimeChallenge = hasStartedResource && instance.PublishedChallengeJson is { } payload
                ? PublishedCourseContentReader.ReadChallenge(payload) : challenge;
            var accepted = runtimeChallenge.Type is ChallengeType.StaticAttachment or ChallengeType.StaticContainer
                ? MatchesStaticFlag(runtimeChallenge, instance, submittedFlag)
                : MatchesFlag(await ResolveExpectedFlagAsync(runtimeChallenge, instance, userId, token), submittedFlag);
            var submission = new ChallengeSubmission
            {
                UserId = userId,
                ChallengeId = challengeId,
                InstanceId = instance.Id,
                SubmittedFlagHash = Hash(submittedFlag),
                Accepted = accepted,
                RejectionCode = accepted ? null : "challenge.flag_incorrect"
            };
            db.ChallengeSubmissions.Add(submission);
            await db.SaveChangesAsync(token);

            if (!accepted)
            {
                if (limitTransaction is not null)
                    await limitTransaction.CommitAsync(token);
                return new ChallengeSubmissionResult(false, false, null, submission.RejectionCode, submission.Id);
            }

            // The limit lock only needs to cover count + insert. Commit the submission
            // before the solve-projection transaction so a later progress conflict
            // cannot erase a recorded attempt.
            if (limitTransaction is not null)
            {
                await limitTransaction.CommitAsync(token);
                await limitTransaction.DisposeAsync();
                limitTransaction = null;
            }

            var mode = await DetermineSolveModeAsync(userId, challengeId, token);
            ChallengeSubmissionResult result;
            await using (var transaction = await db.Database.BeginTransactionAsync(token))
            {
                try
                {
                    await RankingScoreService.LockAsync(db, userId, token);
                    // Merge holds the same lock and may have rewritten identity since flag validation.
                    var existing = await db.ChallengeProgress.AnyAsync(item =>
                        item.UserId == userId && item.ChallengeId == challengeId, token) ||
                        await db.Challenges.AnyAsync(c => c.Id == challengeId && c.PublicationState == ChallengePublicationState.Merged, token);
                    if (existing)
                    {
                        await transaction.CommitAsync(token);
                        result = new ChallengeSubmissionResult(true, false, null, null, submission.Id);
                    }
                    else
                    {
                        var restored = await AdminRankingService.RestoreImportedAsync(db, userId, challengeId, null, token);
                        if (!restored)
                        {
                            db.ChallengeProgress.Add(new ChallengeProgress { UserId = userId, ChallengeId = challengeId, SolveMode = mode });
                            if (await db.RankingSolveCorrections.AnyAsync(c => c.UserId == userId && c.ChallengeId == challengeId && c.ImportedScoreBaselineId == null, token))
                                db.RankingScoreEvents.Add(new RankingScoreEvent { UserId=userId, ChallengeId=challengeId, ScoreDelta=challenge.Score, SolvedDelta=1 });
                        }
                        await dailyProjection.RecordFirstSolveAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow), token);

                        submission.FirstSolve = true;
                        submission.SolveMode = mode;
                        await db.SaveChangesAsync(token);
                        await transaction.CommitAsync(token);
                        result = new ChallengeSubmissionResult(true, true, mode, null, submission.Id);
                    }
                }
                catch (DbUpdateException)
                {
                    await transaction.RollbackAsync(token);
                    db.ChangeTracker.Clear();
                    result = new ChallengeSubmissionResult(true, false, null, null, submission.Id);
                }
            }

            if (result.FirstSolve && rankingCache is not null) AdminRankingService.Invalidate(rankingCache);
            await StopSolvedContainerAsync(runtimeChallenge, userId, challengeId);
            if (result.FirstSolve)
            {
                await dashboardDeltas.PublishFirstSolveAsync(userId, token);
                try
                {
                    var user = await db.Users.AsNoTracking().Where(item => item.Id == userId)
                        .Select(item => new { item.UserName, CohortName = item.Cohort == null ? "" : item.Cohort.Name })
                        .SingleAsync(token);
                    var title = challenge.Localizations.FirstOrDefault(item => item.Locale == "zh-CN")?.Title ??
                                challenge.Localizations.FirstOrDefault(item => item.Locale == "en")?.Title ??
                                challenge.Localizations.FirstOrDefault()?.Title ?? challengeId.ToString();
                    await qqBot.TrySendSolveAsync(new QqSolveEvent(
                        user.UserName ?? userId.ToString(), title, "技能树", user.CohortName), false, token);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    logger.LogWarning(error, "Failed to prepare QQ solve notification");
                }
            }
            return result;
        }
        finally
        {
            if (limitTransaction is not null)
                await limitTransaction.DisposeAsync();
        }
    }

    private async Task StopSolvedContainerAsync(CanonicalChallenge challenge, Guid userId, Guid challengeId)
    {
        if (!challenge.Type.IsContainer())
            return;
        try
        {
            await runtime.StopAsync(userId, challengeId, CancellationToken.None);
        }
        catch (Exception error)
        {
            logger.LogWarning(error,
                "Flag accepted but challenge container could not be stopped for user {UserId} and challenge {ChallengeId}",
                userId, challengeId);
        }
    }

    private async Task<string?> ResolveExpectedFlagAsync(
        CanonicalChallenge challenge, UserChallengeInstance instance, Guid userId, CancellationToken token)
    {
        if (challenge.Type == ChallengeType.DynamicAttachment)
            return (await attachments.GetOrAllocateAsync(userId, challenge.Id, token)).Flag;
        if (challenge.Type == ChallengeType.DynamicContainer)
            return instance.AssignedFlag;
        return challenge.Flags.FirstOrDefault(item => item.Kind == ChallengeFlagKind.Static)?.Value;
    }

    private async Task<ChallengeSolveMode> DetermineSolveModeAsync(
        Guid userId, Guid challengeId, CancellationToken token)
    {
        if (await db.ChallengeHelpUsages.AnyAsync(item => item.UserId == userId &&
                                                           item.ChallengeId == challengeId && item.IsWriteup,
                token))
            return ChallengeSolveMode.AfterWriteup;
        return await db.ChallengeHelpUsages.AnyAsync(item => item.UserId == userId &&
                                                              item.ChallengeId == challengeId &&
                                                              item.HintId != null, token)
            ? ChallengeSolveMode.AfterHint
            : ChallengeSolveMode.Independent;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool MatchesStaticFlag(CanonicalChallenge challenge, UserChallengeInstance instance, string submitted)
    {
        var configured = challenge.Flags.Where(item => item.Kind == ChallengeFlagKind.Static)
            .Aggregate(false, (match, item) => MatchesFlag(item.Value, submitted, IsCaseInsensitive(item.MetadataJson)) | match);
        if (challenge.Type != ChallengeType.StaticContainer || instance.AssignedFlag is null)
            return configured;
        // Whale may inject a random environment value even for a challenge judged by imported static flags.
        using var configuration = System.Text.Json.JsonDocument.Parse(challenge.RuntimeConfigurationJson ?? "{}");
        var whale = configuration.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
            configuration.RootElement.EnumerateObject().Any(p => p.Name.Equals("FlagMode", StringComparison.OrdinalIgnoreCase) &&
                p.Value.ValueKind == System.Text.Json.JsonValueKind.String && p.Value.GetString() == "ctfd-whale");
        return configured || !whale && MatchesFlag(instance.AssignedFlag, submitted,
            IsCaseInsensitive(challenge.Flags.FirstOrDefault(item => item.Value == instance.AssignedFlag)?.MetadataJson));
    }

    private static bool IsCaseInsensitive(string? metadata)
    {
        if (string.IsNullOrEmpty(metadata))
            return false;
        try
        { using var doc = System.Text.Json.JsonDocument.Parse(metadata); return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && doc.RootElement.TryGetProperty("IgnoreCase", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.True; }
        catch (System.Text.Json.JsonException) { return false; }
    }
    private static bool MatchesFlag(string? expected, string submitted, bool ignoreCase = false)
    {
        if (string.IsNullOrEmpty(expected))
            return false;
        if (ignoreCase)
        { expected = expected.ToUpperInvariant(); submitted = submitted.ToUpperInvariant(); }
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(submitted)));
    }
}
