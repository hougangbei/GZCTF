using GZCTF.Features.ChallengeRuntime.Application;
using System.Text;
using System.Text.Json;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.Dashboard.Domain;
using GZCTF.Features.Leaderboard.Domain;
using GZCTF.Features.LearningProgress.Domain;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GZCTF.Features.Leaderboard.Application;

public sealed record RankingPage<T>(IReadOnlyList<T> Items, int Total, int Skip, int Take);
public sealed record RankingAdjustmentCommand(Guid OperationId, string Version, string Kind, long Value, string Reason);
public sealed record RankingReasonCommand(Guid OperationId, string Version, string Reason);
public sealed record RankingVisibilityCommand(Guid OperationId, string Version, bool Hidden, string Reason);
public sealed record RankingLevelsCommand(Guid OperationId, string Version, long[] Thresholds);
public sealed record RankingLevelsPreviewCommand(string Version, long[] Thresholds);
public sealed record RankingOperationResponse(Guid Id, Guid OperationId, Guid UserId, Guid ActorId, string ActorName,
    string Kind, string Reason, long Delta, long BeforeScore, long AfterScore, int SolvedDelta, Guid? ChallengeId,
    DateTimeOffset CreatedAtUtc, Guid? ReversesId, bool Reversed, bool CanReverse, string? DetailsJson = null, string? TargetUserName = null);
public sealed record RankingChallengeResponse(Guid ChallengeId, string Title, int Score, bool Solved, bool Imported, DateTimeOffset? SolvedAtUtc, bool FrozenScoreAvailable = true);
public sealed record RankingDetailResponse(RankingUserResponse User, IReadOnlyList<RankingChallengeResponse> Challenges,
    IReadOnlyList<RankingOperationResponse> Operations);

public sealed class AdminRankingService(AppDbContext db, RankingScoreService scores, IMemoryCache cache,
    ImportedScoreTimelineService timeline,
    GZCTF.Features.Dashboard.Application.DashboardDeltaPublisher? dashboard = null)
{
    public async Task<RankingPage<RankingUserResponse>> ListAsync(string? search, int skip, int take, bool? hidden, bool? banned, CancellationToken token)
    {
        ValidatePage(skip, take);
        var rows = (await scores.SnapshotAsync(token)).Select(s => s.User).Where(u =>
            (string.IsNullOrWhiteSpace(search) || u.UserName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)) &&
            (hidden == null || u.Hidden == hidden) && (banned == null || (u.Role == "Banned") == banned))
            .OrderByDescending(u => u.Score).ThenByDescending(u => u.SolvedCount).ThenBy(u => u.UserName, StringComparer.Ordinal).ToArray();
        return new(rows.Skip(skip).Take(take).ToArray(), rows.Length, skip, take);
    }
    public async Task<RankingDetailResponse> DetailAsync(Guid userId, CancellationToken token)
    {
        var user = (await scores.SnapshotAsync(token)).SingleOrDefault(s => s.User.UserId == userId)?.User
            ?? throw new KeyNotFoundException();
        var progress = await db.ChallengeProgress.AsNoTracking().Where(p => p.UserId == userId).ToDictionaryAsync(p => p.ChallengeId, token);
        var unavailableFrozenScores = new HashSet<Guid>();
        foreach (var imported in progress.Values.Where(p => p.ImportedScoreBaselineId != null && p.ImportedFrozenScore == null))
        {
            try { imported.ImportedFrozenScore = await timeline.FrozenScoreAsync(imported, token); }
            catch (RankingValidationException) { unavailableFrozenScores.Add(imported.ChallengeId); }
        }
        var corrections = await db.RankingSolveCorrections.AsNoTracking().Where(c => c.UserId == userId).ToDictionaryAsync(c => c.ChallengeId, token);
        var published = await PublishedCourseContentReader.GetChallengesAsync(db, token);
        var challenges = await db.Challenges.AsNoTracking().Include(c => c.Localizations)
            .Where(c => c.PublicationState != ChallengePublicationState.Merged).ToArrayAsync(token);
        challenges = challenges.Where(c => published.ContainsKey(c.Id) || progress.ContainsKey(c.Id) || corrections.ContainsKey(c.Id))
            .Select(c => published.GetValueOrDefault(c.Id) ?? c).ToArray();
        return new(user, challenges.Select(c =>
        {
            progress.TryGetValue(c.Id, out var p);
            corrections.TryGetValue(c.Id, out var correction);
            return new RankingChallengeResponse(c.Id, c.Localizations.FirstOrDefault(l => l.Locale == "zh-CN")?.Title ?? c.Localizations.FirstOrDefault()?.Title ?? c.Id.ToString(),
                unavailableFrozenScores.Contains(c.Id) ? 0 : p?.ImportedFrozenScore ?? (correction?.ImportedScoreBaselineId != null ? correction.FrozenScore : published.GetValueOrDefault(c.Id)?.Score ?? 0), p != null,
                p?.ImportedScoreBaselineId != null || correction?.ImportedScoreBaselineId != null, p?.SolvedAtUtc, !unavailableFrozenScores.Contains(c.Id));
        }).OrderBy(c => c.Title).ToArray(), (await OperationsAsync(userId, 0, 100, token)).Items);
    }
    public async Task<RankingPage<RankingOperationResponse>> OperationsAsync(Guid? userId, int skip, int take, CancellationToken token)
    {
        ValidatePage(skip, take);
        var query = db.RankingOperations.AsNoTracking().Where(o => userId == null || o.UserId == userId);
        var total = await query.CountAsync(token);
        var rows = await query.OrderByDescending(o => o.CreatedAtUtc).ThenByDescending(o => o.Id).Skip(skip).Take(take).ToArrayAsync(token);
        var ids = rows.Select(o => o.Id).ToArray();
        var userIds = rows.Select(o => o.UserId).Distinct().ToArray();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.UserName }).ToDictionaryAsync(u => u.Id, u => u.UserName, token);
        var reversed = await db.RankingOperations.AsNoTracking().Where(o => o.ReversesId != null && ids.Contains(o.ReversesId.Value)).Select(o => o.ReversesId!.Value).ToArrayAsync(token);
        return new(rows.Select(o => new RankingOperationResponse(o.Id, o.OperationId, o.UserId, o.ActorId, o.ActorName, o.Kind, o.Reason, o.AfterScore - o.BeforeScore, o.BeforeScore, o.AfterScore, EffectiveSolvedDelta(o), o.ChallengeId, o.CreatedAtUtc, o.ReversesId, reversed.Contains(o.Id), o.Kind is "delta" or "target" && !reversed.Contains(o.Id), o.DetailsJson, names.GetValueOrDefault(o.UserId))).ToArray(), total, skip, take);
    }
    public Task<RankingDetailResponse> AdjustAsync(Guid userId, RankingAdjustmentCommand c, Guid actorId, string actorName, CancellationToken token)
    {
        if (c.Kind is not ("delta" or "target") || c.Value is < int.MinValue or > int.MaxValue)
            throw new RankingValidationException("ranking.invalid_adjustment");
        return MutateAsync(userId, c.OperationId, c.Version, c.Reason, c.Kind, c, actorId, actorName, async (row, op) =>
        {
            op.Delta = c.Kind == "target" ? c.Value - row.Score : c.Value;
            await Task.CompletedTask;
        }, token);
    }
    public async Task<RankingDetailResponse> ReverseAsync(Guid id, RankingReasonCommand c, Guid actorId, string actorName, CancellationToken token)
    {
        var original = await db.RankingOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, token) ?? throw new KeyNotFoundException();
        return await MutateAsync(original.UserId, c.OperationId, c.Version, c.Reason, "reverse", new { id, c }, actorId, actorName, async (row, op) =>
        {
            if (original.Kind is not ("delta" or "target") || await db.RankingOperations.AnyAsync(o => o.ReversesId == id, token))
                throw new RankingConflictException("ranking.already_reversed_or_not_adjustment");
            op.Delta = -original.Delta;
            op.ReversesId = id;
        }, token);
    }
    public Task<RankingDetailResponse> VisibilityAsync(Guid userId, RankingVisibilityCommand c, Guid actorId, string actorName, CancellationToken token) =>
        MutateAsync(userId, c.OperationId, c.Version, c.Reason, "visibility", c, actorId, actorName, async (row, op) =>
        {
            var state = await StateAsync(userId, token);
            state.Hidden = c.Hidden;
            op.DetailsJson = JsonSerializer.Serialize(new { beforeHidden = row.Hidden, afterHidden = c.Hidden, sourceHidden = row.SourceHidden });
        }, token);
    public Task<RankingDetailResponse> SolveAsync(Guid userId, Guid challengeId, bool solved, RankingReasonCommand c, Guid actorId, string actorName, CancellationToken token) =>
        MutateAsync(userId, c.OperationId, c.Version, c.Reason, solved ? "mark_solve" : "revoke_solve", new { challengeId, solved, c }, actorId, actorName, async (row, op) =>
        {
            op.ChallengeId = challengeId;
            var challenge = await db.Challenges.SingleOrDefaultAsync(ch => ch.Id == challengeId, token) ?? throw new KeyNotFoundException();
            if (challenge.PublicationState == ChallengePublicationState.Merged)
                throw new RankingValidationException("ranking.challenge_merged");
            var published = await PublishedCourseContentReader.GetChallengeAsync(db, challengeId, token);
            var progress = await db.ChallengeProgress.SingleOrDefaultAsync(p => p.UserId == userId && p.ChallengeId == challengeId, token);
            // Imported corrections retain their frozen baseline. Native changes use current published values.
            var effectiveScore = published?.Score ?? 0;
            if (solved)
            {
                if (progress != null)
                    return;
                if (published is null && !await db.RankingSolveCorrections.AnyAsync(correction =>
                        correction.UserId == userId && correction.ChallengeId == challengeId && correction.ImportedScoreBaselineId != null, token))
                    throw new RankingValidationException("ranking.challenge_not_published");
                var restored = await RestoreImportedAsync(db, userId, challengeId, op, token);
                if (!restored)
                {
                    db.ChallengeProgress.Add(new ChallengeProgress { UserId = userId, ChallengeId = challengeId });
                    if (await db.RankingSolveCorrections.AnyAsync(c => c.UserId == userId && c.ChallengeId == challengeId && c.ImportedScoreBaselineId == null, token))
                        db.RankingScoreEvents.Add(new RankingScoreEvent { UserId = userId, ChallengeId = challengeId, ScoreDelta = effectiveScore, SolvedDelta = 1 });
                }
            }
            else
            {
                if (progress == null)
                    return;
                var correction = await db.RankingSolveCorrections.SingleOrDefaultAsync(p => p.UserId == userId && p.ChallengeId == challengeId, token);
                if (correction == null)
                {
                    correction = new RankingSolveCorrection
                    {
                        UserId = userId,
                        ChallengeId = challengeId,
                        ImportedScoreBaselineId = progress.ImportedScoreBaselineId,
                        FrozenScore = progress.ImportedScoreBaselineId == null ? effectiveScore : progress.ImportedFrozenScore ?? await timeline.FrozenScoreAsync(progress, token),
                        OriginalSolvedAtUtc = progress.SolvedAtUtc,
                        AttributionMetadataJson = progress.AttributionMetadataJson
                    };
                    db.RankingSolveCorrections.Add(correction);
                    if (correction.ImportedScoreBaselineId == null)
                        db.RankingScoreEvents.Add(new RankingScoreEvent { UserId = userId, ChallengeId = challengeId, ScoreDelta = effectiveScore, SolvedDelta = 1, AtUtc = progress.SolvedAtUtc });
                }
                correction.Revoked = true;
                if (correction.ImportedScoreBaselineId != null)
                { op.Delta = -correction.FrozenScore; op.SolvedDelta = -1; }
                if (correction.ImportedScoreBaselineId == null)
                    db.RankingScoreEvents.Add(new RankingScoreEvent { UserId = userId, ChallengeId = challengeId, ScoreDelta = -effectiveScore, SolvedDelta = -1 });
                db.ChallengeProgress.Remove(progress);
            }
            await db.SaveChangesAsync(token);
            await RebuildUserDailyAsync(db, userId, token);
        }, token);

    private async Task<RankingDetailResponse> MutateAsync(Guid userId, Guid operationId, string version, string reason, string kind, object payload,
        Guid actorId, string actorName, Func<RankingUserResponse, RankingOperation, Task> apply, CancellationToken token)
    {
        ValidateReason(operationId, reason);
        var hash = RankingScoreService.Hash(JsonSerializer.Serialize(new { userId, kind, payload, actorId }));
        await using var tx = await db.Database.BeginTransactionAsync(token);
        await RankingScoreService.LockAsync(db, userId, token);
        try
        {
            var prior = await db.RankingOperations.AsNoTracking().SingleOrDefaultAsync(o => o.OperationId == operationId, token);
            if (prior != null)
            {
                if (prior.PayloadHash != hash)
                    throw new RankingConflictException("ranking.operation_payload_mismatch");
                await tx.CommitAsync(token);
                return await DetailAsync(userId, token);
            }
            var row = (await scores.SnapshotAsync(token)).SingleOrDefault(s => s.User.UserId == userId)?.User ?? throw new KeyNotFoundException();
            if (row.Version != version)
                throw new RankingConflictException();
            var op = new RankingOperation { OperationId = operationId, PayloadHash = hash, UserId = userId, ActorId = actorId, ActorName = actorName, Kind = kind, Reason = reason.Trim(), BeforeScore = row.Score };
            await apply(row, op);
            var state = await StateAsync(userId, token);
            state.Revision++;
            db.RankingOperations.Add(op);
            await db.SaveChangesAsync(token);
            var updated = (await scores.SnapshotAsync(token)).Single(s => s.User.UserId == userId).User;
            op.AfterScore = updated.Score;
            if (kind is "mark_solve" or "revoke_solve")
                op.DetailsJson = JsonSerializer.Serialize(new { beforeSolvedCount = row.SolvedCount, afterSolvedCount = updated.SolvedCount });
            if (kind is "delta" or "target" or "reverse" && op.AfterScore is < int.MinValue or > int.MaxValue)
                throw new RankingValidationException("ranking.score_out_of_range");
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
        }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
        Invalidate(cache);
        if (dashboard is not null && kind is "mark_solve" or "revoke_solve")
        {
            try
            { await dashboard.PublishFirstSolveAsync(userId, token); }
            catch (Exception error) when (error is not OperationCanceledException) { /* The committed projection remains authoritative; clients refresh on reconnect. */ }
        }
        return await DetailAsync(userId, token);
    }
    internal static async Task<bool> RestoreImportedAsync(AppDbContext db, Guid userId, Guid challengeId, RankingOperation? op, CancellationToken token)
    {
        var correction = await db.RankingSolveCorrections.SingleOrDefaultAsync(c => c.UserId == userId && c.ChallengeId == challengeId, token);
        if (correction is not { Revoked: true })
            return false;
        correction.Revoked = false;
        if (correction.ImportedScoreBaselineId == null)
            return false;
        db.ChallengeProgress.Add(new ChallengeProgress
        {
            UserId = userId,
            ChallengeId = challengeId,
            ImportedScoreBaselineId = correction.ImportedScoreBaselineId,
            ImportedFrozenScore = correction.FrozenScore,
            SolvedAtUtc = DateTimeOffset.UtcNow,
            AttributionMetadataJson = correction.AttributionMetadataJson
        });
        if (op == null)
        {
            op = new RankingOperation { OperationId = Guid.NewGuid(), UserId = userId, ActorId = userId, ActorName = "学员", Kind = "restore_solve", Reason = "重新完成已撤销通关", ChallengeId = challengeId };
            db.RankingOperations.Add(op);
            var before = (await new RankingScoreService(db).SnapshotAsync(token)).Single(s => s.User.UserId == userId).User.Score;
            op.BeforeScore = before;
            op.AfterScore = before + correction.FrozenScore;
        }
        op.Delta = correction.FrozenScore;
        op.SolvedDelta = 1;
        return true;
    }
    internal static async Task RebuildUserDailyAsync(AppDbContext db, Guid userId, CancellationToken token)
    {
        var old = await db.LearnerDailySolveStats.Where(s => s.UserId == userId).ToArrayAsync(token);
        db.LearnerDailySolveStats.RemoveRange(old);
        await db.SaveChangesAsync(token);
        var progress = await db.ChallengeProgress.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.SolvedAtUtc).ToArrayAsync(token);
        db.LearnerDailySolveStats.AddRange(progress.GroupBy(t => DateOnly.FromDateTime(t.UtcDateTime)).Select(g => new LearnerDailySolveStat { UserId = userId, Date = g.Key, SolveCount = g.Count() }));
    }
    private async Task<RankingUserState> StateAsync(Guid id, CancellationToken token)
    {
        var state = db.RankingUserStates.Local.SingleOrDefault(s => s.UserId == id) ?? await db.RankingUserStates.SingleOrDefaultAsync(s => s.UserId == id, token);
        if (state != null)
            return state;
        state = new() { UserId = id };
        db.RankingUserStates.Add(state);
        return state;
    }
    public async Task<int> PreviewLevelsAsync(RankingLevelsPreviewCommand c, CancellationToken token)
    {
        RankingScoreService.ValidateThresholds(c.Thresholds);
        if ((await scores.LevelsAsync(token)).Version != c.Version)
            throw new RankingConflictException();
        return (await scores.SnapshotAsync(token)).Count(s => s.User.Level != RankingScoreService.Level(s.User.Score, c.Thresholds));
    }
    public async Task<RankingLevelResponse> SetLevelsAsync(RankingLevelsCommand c, Guid actorId, string actorName, CancellationToken token)
    {
        RankingScoreService.ValidateThresholds(c.Thresholds);
        ValidateReason(c.OperationId, "等级门槛变更");
        var hash = RankingScoreService.Hash(JsonSerializer.Serialize(new { kind = "levels", c, actorId }));
        await using var tx = await db.Database.BeginTransactionAsync(token);
        await RankingScoreService.LockAsync(db, Guid.Empty, token);
        try
        {
            var prior = await db.RankingOperations.AsNoTracking().SingleOrDefaultAsync(o => o.OperationId == c.OperationId, token);
            if (prior != null)
            { if (prior.PayloadHash != hash) throw new RankingConflictException("ranking.operation_payload_mismatch"); await tx.CommitAsync(token); return await scores.LevelsAsync(token); }
            if ((await scores.LevelsAsync(token)).Version != c.Version)
                throw new RankingConflictException();
            var settings = await db.RankingLevelSettings.SingleOrDefaultAsync(s => s.Id == 1, token);
            if (settings == null)
            { settings = new(); db.RankingLevelSettings.Add(settings); }
            var beforeJson = settings.ThresholdsJson;
            settings.Revision++;
            settings.ThresholdsJson = JsonSerializer.Serialize(c.Thresholds);
            db.RankingOperations.Add(new RankingOperation { OperationId = c.OperationId, PayloadHash = hash, ActorId = actorId, ActorName = actorName, Kind = "levels", Reason = "等级门槛变更", DetailsJson = JsonSerializer.Serialize(new { before = JsonSerializer.Deserialize<long[]>(beforeJson), after = c.Thresholds }) });
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
        }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
        Invalidate(cache);
        return await scores.LevelsAsync(token);
    }
    public async Task<byte[]> ExportAsync(string? search, bool? hidden, bool? banned, CancellationToken token)
    {
        var rows = (await scores.SnapshotAsync(token)).Select(s => s.User).Where(u => (string.IsNullOrWhiteSpace(search) || u.UserName.Contains(search, StringComparison.OrdinalIgnoreCase)) && (hidden == null || u.Hidden == hidden) && (banned == null || (u.Role == "Banned") == banned)).OrderByDescending(u => u.Score);
        var text = new StringBuilder("用户名,等级,个性签名,解题数,累计积分,隐藏,角色\r\n");
        foreach (var u in rows)
            text.AppendLine(string.Join(",", new[] { u.UserName, $"Lv.{u.Level}", u.Bio, u.SolvedCount.ToString(), u.Score.ToString(), u.Hidden.ToString(), u.Role }.Select(CsvCell)));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray();
    }
    private static int EffectiveSolvedDelta(RankingOperation operation)
    {
        if (operation.Kind is not ("mark_solve" or "revoke_solve") || operation.DetailsJson is null)
            return operation.SolvedDelta;
        using var details = JsonDocument.Parse(operation.DetailsJson);
        return details.RootElement.GetProperty("afterSolvedCount").GetInt32() - details.RootElement.GetProperty("beforeSolvedCount").GetInt32();
    }

    public static string CsvCell(string value)
    {
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@') || value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n'))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private static void ValidatePage(int skip, int take) { if (skip < 0 || take is < 1 or > 100) throw new RankingValidationException("ranking.invalid_page"); }
    private static void ValidateReason(Guid id, string reason) { if (id == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000) throw new RankingValidationException("ranking.reason_required"); }
    internal static void Invalidate(IMemoryCache cache) { cache.Remove("learning-leaderboard:score"); cache.Remove("learning-leaderboard:solves"); }
}
