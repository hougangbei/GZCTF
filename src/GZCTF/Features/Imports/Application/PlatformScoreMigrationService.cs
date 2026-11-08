using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Features.Dashboard.Domain;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.Imports.Domain;
using GZCTF.Features.LearningProgress.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.Imports.Application;

// Runs under PlatformImportService's transaction and source/account advisory locks.
public sealed class PlatformScoreMigrationService(AppDbContext db)
{
    public static string SkipUserIdsHash(PlatformImportOptions options) => Convert.ToHexStringLower(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes((options.SkipUserIds ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())));

    public async Task<IReadOnlyList<PlatformImportIssue>> PreviewAsync(PlatformImportPackage package,
        PlatformImportOptions options, CancellationToken token = default)
    {
        List<PlatformImportIssue> issues = [];
        if (!options.MergeScores) return issues;
        void Error(string code, string id = "") => issues.Add(new("error", code, id, code switch
        {
            "score_merge_options_invalid" => "积分合并仅支持 CTFd 备份；请同时启用账号和历史记录导入。",
            "score_merge_requires_users_mode" => "积分合并要求来源为个人赛（user_mode=users）；请确认来源赛制并重新导出。",
            "score_merge_frozen_scoreboard" => "来源排行榜配置了冻结时间，无法确认最终总分；请先解除冻结并重新导出。",
            "score_merge_team_history" => "历史解题或奖励归属于团队，无法安全转换为个人积分；请保留为历史档案。",
            "score_merge_invalid_reference" => "历史解题或奖励缺少有效的账号、题目引用；请修复来源记录后重新导出。",
            "score_merge_missing_date" => "正确解题或奖励缺少时间，无法恢复进度及排名顺序；请补全来源时间后重新导出。",
            "score_merge_source_changed" => "此来源已建立积分基线，但备份或跳过账号列表已改变；请使用原备份及原跳过列表重试，新增资料可仅导入历史。",
            "score_merge_user_already_has_baseline" => "目标账号已有积分基线，不能再次叠加来源总分；请核对账号映射并仅导入历史。",
            "score_merge_native_progress_conflict" => "目标账号已完成对应的本地题目，无法安全合并原站积分；请先人工核对本地进度及来源映射。",
            "score_merge_challenge_mapping_invalid" => "来源题目映射的本地题目已合并、删除或不存在；请先人工修复来源映射，再重新预检，避免重复计分。",
            "score_merge_score_overflow" => "来源账号总分超出可保存范围；请核对题目分值和奖励后重新导出。",
            _ => "原站积分合并条件不满足；请检查来源赛制、历史数据及已导入记录。"
        }));
        if (package.Format != "CTFd" || !options.IncludeAccounts || !options.IncludeHistory)
            Error("score_merge_options_invalid");
        var explicitUserMode = false;
        using var metadata = JsonDocument.Parse(package.MetadataJson);
        if (metadata.RootElement.TryGetProperty("config", out var configs) && configs.ValueKind == JsonValueKind.Array)
        foreach (var config in configs.EnumerateArray())
        {
            var key = config.TryGetProperty("key", out var k) ? k.ToString() : "";
            var value = config.TryGetProperty("value", out var v) ? v.ToString() : "";
            if (key == "user_mode")
            {
                explicitUserMode = true;
                if (value != "users") Error("score_merge_requires_users_mode");
            }
            if (key == "freeze" && value is not ("" or "0" or "null" or "false" or "False")) Error("score_merge_frozen_scoreboard");
        }
        if (!explicitUserMode && package.Users.Any(u => u.TeamId is not null))
            Error("score_merge_requires_users_mode");
        if (package.Submissions.Any(s => s.TeamId is not null) || package.Awards.Any(a => a.TeamId is not null))
            Error("score_merge_team_history");
        var userIds = package.Users.Select(u => u.SourceId).ToHashSet(StringComparer.Ordinal);
        var challengeIds = package.Challenges.Select(c => c.SourceId).ToHashSet(StringComparer.Ordinal);
        foreach (var solve in package.Submissions.Where(s => s.Result == "correct"))
        {
            if (solve.UserId is null || !userIds.Contains(solve.UserId) || !challengeIds.Contains(solve.ChallengeId))
                Error("score_merge_invalid_reference", solve.SourceId);
            if (solve.SubmittedAtUtc is null) Error("score_merge_missing_date", solve.SourceId);
        }
        foreach (var award in package.Awards)
        {
            if (award.UserId is null || !userIds.Contains(award.UserId)) Error("score_merge_invalid_reference", award.SourceId);
            if (award.AwardedAtUtc is null) Error("score_merge_missing_date", award.SourceId);
        }
        var prior = await db.PlatformScoreMigrations.AsNoTracking().SingleOrDefaultAsync(m => m.SourceInstance == options.SourceInstance, token);
        if (prior is not null)
        {
            if (prior.ArchiveFingerprint != package.Fingerprint || prior.CanonicalSkipUserIdsHash != SkipUserIdsHash(options))
                Error("score_merge_source_changed");
            return issues;
        }
        var maps = await db.PlatformImportMaps.AsNoTracking().Where(m => m.SourceInstance == options.SourceInstance).ToArrayAsync(token);
        var skipped = (options.SkipUserIds ?? []).ToHashSet(StringComparer.Ordinal);
        var users = maps.Where(m => m.Kind == "user" && userIds.Contains(m.SourceId) && !skipped.Contains(m.SourceId)).ToDictionary(m => m.SourceId, m => m.TargetId);
        var challenges = maps.Where(m => m.Kind == "challenge").ToDictionary(m => m.SourceId, m => m.TargetId);
        var mappedChallenges = maps.Where(m => m.Kind == "challenge" && challengeIds.Contains(m.SourceId)).ToArray();
        var mappedChallengeIds = mappedChallenges.Select(m => m.TargetId).ToArray();
        var canonicalStates = await db.Challenges.AsNoTracking().Where(c => mappedChallengeIds.Contains(c.Id))
            .Select(c => new { c.Id, c.PublicationState }).ToDictionaryAsync(c => c.Id, token);
        foreach (var mapping in mappedChallenges)
            if (!canonicalStates.TryGetValue(mapping.TargetId, out var canonical) || canonical.PublicationState == ChallengePublicationState.Merged)
                Error("score_merge_challenge_mapping_invalid", mapping.SourceId);
        var targetUserIds = users.Values.ToArray();
        if (await db.PlatformScoreBaselines.AnyAsync(b => targetUserIds.Contains(b.UserId), token)) Error("score_merge_user_already_has_baseline");
        var progress = await db.ChallengeProgress.AsNoTracking().Where(p => targetUserIds.Contains(p.UserId)).Select(p => new { p.UserId, p.ChallengeId }).ToArrayAsync(token);
        foreach (var solve in package.Submissions.Where(s => s.Result == "correct" && s.UserId is not null))
            if (users.TryGetValue(solve.UserId!, out var user) && challenges.TryGetValue(solve.ChallengeId, out var challenge) &&
                progress.Any(p => p.UserId == user && p.ChallengeId == challenge)) Error("score_merge_native_progress_conflict", solve.SourceId);
        foreach (var source in package.Users.Where(u => !skipped.Contains(u.SourceId)))
        {
            var solveScore = package.Submissions.Where(s => s.Result == "correct" && s.UserId == source.SourceId)
                .Select(s => s.ChallengeId).Distinct(StringComparer.Ordinal).Sum(id => (long)(package.Challenges.FirstOrDefault(c => c.SourceId == id)?.Score ?? 0));
            var total = solveScore + package.Awards.Where(a => a.UserId == source.SourceId).Sum(a => (long)a.Value);
            if (total is < int.MinValue or > int.MaxValue) Error("score_merge_score_overflow", source.SourceId);
        }
        return issues;
    }

    public async Task ImportAsync(PlatformImportPackage package, PlatformImportOptions options, PlatformImportBatch batch,
        CancellationToken token = default)
    {
        if (!options.MergeScores) return;
        var issues = await PreviewAsync(package, options, token);
        if (issues.Any(i => i.Severity == "error")) throw new InvalidOperationException("import.score_merge_preflight_failed");
        if (await db.PlatformScoreMigrations.AnyAsync(m => m.SourceInstance == options.SourceInstance, token)) return;
        var maps = await db.PlatformImportMaps.Where(m => m.SourceInstance == options.SourceInstance).ToArrayAsync(token);
        var users = maps.Where(m => m.Kind == "user").ToDictionary(m => m.SourceId, m => m.TargetId);
        var challenges = maps.Where(m => m.Kind == "challenge").ToDictionary(m => m.SourceId, m => m.TargetId);
        var sourceChallenges = package.Challenges.ToDictionary(c => c.SourceId);
        var skipped = (options.SkipUserIds ?? []).ToHashSet(StringComparer.Ordinal);
        List<Guid> affected = [];
        foreach (var source in package.Users.Where(u => !skipped.Contains(u.SourceId)))
        {
            var solves = package.Submissions.Where(s => s.Result == "correct" && s.UserId == source.SourceId)
                .GroupBy(s => s.ChallengeId).Select(g => g.OrderBy(s => s.SubmittedAtUtc).First()).ToArray();
            var awards = package.Awards.Where(a => a.UserId == source.SourceId).ToArray();
            using var metadata = JsonDocument.Parse(source.MetadataJson);
            var baseline = new PlatformScoreBaseline
            {
                SourceInstance = options.SourceInstance, SourceUserId = source.SourceId, UserId = users[source.SourceId], BatchId = batch.Id,
                Score = checked((int)(solves.Sum(s => (long)sourceChallenges[s.ChallengeId].Score) + awards.Sum(a => (long)a.Value))),
                SolvedCount = solves.Length, HasActivity = solves.Length > 0 || awards.Length > 0,
                Hidden = metadata.RootElement.TryGetProperty("hidden", out var hidden) && hidden.ToString() is "true" or "True" or "1",
                LastScoringAtUtc = solves.Where(s => sourceChallenges[s.ChallengeId].Score != 0).Select(s => s.SubmittedAtUtc)
                    .Concat(awards.Select(a => a.AwardedAtUtc)).DefaultIfEmpty().Max()
            };
            db.PlatformScoreBaselines.Add(baseline);
            affected.Add(baseline.UserId);
            foreach (var solve in solves)
                db.ChallengeProgress.Add(new ChallengeProgress
                {
                    UserId = baseline.UserId, ChallengeId = challenges[solve.ChallengeId], SolvedAtUtc = solve.SubmittedAtUtc!.Value,
                    SolveMode = ChallengeSolveMode.Independent, ImportedScoreBaseline = baseline, ImportedFrozenScore = sourceChallenges[solve.ChallengeId].Score,
                    AttributionMetadataJson = JsonSerializer.Serialize(new { Source = "platform-score-baseline", options.SourceInstance, batch.Id, solve.SourceId })
                });
        }
        db.PlatformScoreMigrations.Add(new PlatformScoreMigration
        {
            SourceInstance = options.SourceInstance, ArchiveFingerprint = package.Fingerprint,
            CanonicalSkipUserIdsHash = SkipUserIdsHash(options), BatchId = batch.Id
        });
        await db.SaveChangesAsync(token);
        var affectedIds = affected.ToArray();
        var oldStats = await db.LearnerDailySolveStats.Where(s => affectedIds.Contains(s.UserId)).ToArrayAsync(token);
        db.LearnerDailySolveStats.RemoveRange(oldStats);
        await db.SaveChangesAsync(token);
        var progress = await db.ChallengeProgress.AsNoTracking().Where(p => affectedIds.Contains(p.UserId)).Select(p => new { p.UserId, p.SolvedAtUtc }).ToArrayAsync(token);
        db.LearnerDailySolveStats.AddRange(progress.GroupBy(p => new { p.UserId, Date = DateOnly.FromDateTime(p.SolvedAtUtc.UtcDateTime) })
            .Select(g => new LearnerDailySolveStat { UserId = g.Key.UserId, Date = g.Key.Date, SolveCount = g.Count() }));
    }
}
