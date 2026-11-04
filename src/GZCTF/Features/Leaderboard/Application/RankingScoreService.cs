using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.Leaderboard.Domain;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.Leaderboard.Application;

public sealed class RankingConflictException(string message = "ranking.version_conflict") : Exception(message);
public sealed class RankingValidationException(string message) : Exception(message);
public sealed record RankingLevelResponse(string Version, long[] Thresholds);
public sealed record RankingUserResponse(Guid UserId, string UserName, string Bio, string? Avatar, string Role,
    bool Hidden, bool SourceHidden, bool? VisibilityOverride, long Score, long ImportedScore, long EarnedScore,
    long AdjustmentScore, int SolvedCount, int ImportedSolvedCount, int EarnedSolvedCount, int Level, string Version);
public sealed record RankingNativeSolve(Guid ChallengeId, DateTimeOffset SolvedAtUtc, int Score);
public sealed record RankingSnapshot(RankingUserResponse User, bool HasActivity, DateTimeOffset LastScoringAtUtc,
    RankingNativeSolve[] Solves);

public sealed class RankingScoreService(AppDbContext db)
{
    public static readonly long[] DefaultThresholds = [0, 1000, 3000, 6000, 10000, 15000, 22000, 30000, 40000, 50000];
    public static int Level(long score, IReadOnlyList<long> thresholds)
    {
        var level = 1;
        for (var i = 1; i < thresholds.Count && score >= thresholds[i]; i++)
            level = i + 1;
        return level;
    }
    public static void ValidateThresholds(long[] thresholds)
    {
        if (thresholds.Length is < 1 or > 100 || thresholds[0] != 0 ||
            thresholds.Any(t => t < 0 || t > int.MaxValue) || thresholds.Zip(thresholds.Skip(1)).Any(p => p.First >= p.Second))
            throw new RankingValidationException("ranking.invalid_thresholds");
    }
    public async Task<RankingLevelResponse> LevelsAsync(CancellationToken token)
    {
        var row = await db.RankingLevelSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == 1, token);
        var thresholds = row is null ? DefaultThresholds.ToArray() : JsonSerializer.Deserialize<long[]>(row.ThresholdsJson)!;
        ValidateThresholds(thresholds);
        return new((row?.Revision ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture), thresholds);
    }
    public async Task<RankingSnapshot[]> SnapshotAsync(CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, token);
            var snapshot = await SnapshotAsync(token);
            await transaction.CommitAsync(token);
            return snapshot;
        }
        var eligibleIds = await db.SkillTreeRevisions.AsNoTracking()
            .Where(r => r.SkillTree.DeletedAtUtc == null && r.SkillTree.CurrentPublishedRevisionId == r.Id)
            .SelectMany(r => r.Categories).Where(r => r.Category.DeletedAtUtc == null)
            .SelectMany(r => r.Category.Skills).Where(r => r.Skill.DeletedAtUtc == null && r.Skill.IsEnabled)
            .SelectMany(r => r.Skill.Contents)
            .Where(c => c.ChallengeId != null && c.Challenge!.PublicationState == ChallengePublicationState.Published && c.Challenge.IsEnabled)
            .Select(c => c.ChallengeId!.Value).Distinct().ToArrayAsync(token);
        var users = await db.Users.AsNoTracking().Select(u => new { u.Id, u.UserName, u.Bio, u.AvatarHash, u.Role }).ToArrayAsync(token);
        var progress = await db.ChallengeProgress.AsNoTracking().Select(p => new { p.Id, p.UserId, p.ChallengeId, p.SolvedAtUtc, p.ImportedScoreBaselineId, p.Challenge.Score }).ToArrayAsync(token);
        var baselines = await db.PlatformScoreBaselines.AsNoTracking().ToDictionaryAsync(b => b.UserId, token);
        var operations = (await db.RankingOperations.AsNoTracking().ToArrayAsync(token)).ToLookup(o => o.UserId);
        var states = await db.RankingUserStates.AsNoTracking().ToDictionaryAsync(s => s.UserId, token);
        var levels = await LevelsAsync(token);
        var byUser = progress.ToLookup(p => p.UserId);
        var eligible = eligibleIds.ToHashSet();
        return users.Select(user =>
        {
            var allProgress = byUser[user.Id].OrderBy(p => p.Id).ToArray();
            var native = allProgress.Where(p => p.ImportedScoreBaselineId == null && eligible.Contains(p.ChallengeId))
                .Select(p => new RankingNativeSolve(p.ChallengeId, p.SolvedAtUtc, p.Score)).ToArray();
            baselines.TryGetValue(user.Id, out var baseline);
            states.TryGetValue(user.Id, out var state);
            var journal = operations[user.Id].ToArray();
            var adjustment = journal.Sum(o => o.Delta);
            var earned = native.Sum(p => (long)p.Score);
            var score = (baseline?.Score ?? 0L) + earned + adjustment;
            var solved = checked((baseline?.SolvedCount ?? 0) + native.Length + journal.Sum(o => o.SolvedDelta));
            var version = Hash(JsonSerializer.Serialize(new
            {
                baseline = baseline is null ? "" : $"{baseline.Id}:{baseline.Score}:{baseline.SolvedCount}:{baseline.Hidden}",
                state = state?.Revision ?? 0,
                state?.Hidden,
                user.Role,
                score,
                solved,
                progress = allProgress.Select(p => new { p.Id, p.Score, p.SolvedAtUtc, p.ImportedScoreBaselineId }),
                levels.Version
            }));
            return new RankingSnapshot(new(user.Id, user.UserName ?? "", user.Bio, user.AvatarHash == null ? null : $"/assets/{user.AvatarHash}/avatar", user.Role.ToString(),
                state?.Hidden ?? baseline?.Hidden ?? false, baseline?.Hidden ?? false, state?.Hidden,
                score, baseline?.Score ?? 0, earned, adjustment, solved, baseline?.SolvedCount ?? 0, native.Length,
                Level(score, levels.Thresholds), version), native.Length > 0 || baseline?.HasActivity == true || journal.Any(o => o.Delta != 0 || o.SolvedDelta != 0 || o.BeforeScore != o.AfterScore),
                native.Select(p => p.SolvedAtUtc).Concat(journal.Where(o => o.Delta != 0 || o.SolvedDelta != 0 || o.BeforeScore != o.AfterScore).Select(o => o.CreatedAtUtc)).Append(baseline?.LastScoringAtUtc ?? DateTimeOffset.MinValue).Max(), native);
        }).ToArray();
    }
    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    // Same account lock as score import; every solve/admin mutation acquires it before the user lock.
    public static async Task LockAsync(AppDbContext db, Guid userId, CancellationToken token)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({-329574250683364781L})", token);
        var key = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(userId.ToByteArray()));
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", token);
    }
}
