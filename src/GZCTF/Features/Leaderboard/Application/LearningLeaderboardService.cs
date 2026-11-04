using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GZCTF.Features.Leaderboard.Application;

public sealed record LearningLeaderboardEntry(
    int Rank, string UserName, int SolvedCount, long Score,
    int ImportedScore = 0, long EarnedScore = 0, int ImportedSolvedCount = 0, int EarnedSolvedCount = 0);

public sealed record LearningLeaderboardPoint(DateOnly Date, int SolvedCount, long Score);
public sealed record LearningLeaderboardSeries(string UserName, IReadOnlyList<LearningLeaderboardPoint> Points);
public sealed record LearningLeaderboardResponse(string Metric, DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<LearningLeaderboardEntry> Entries, IReadOnlyList<LearningLeaderboardSeries> Series,
    bool HasImportedScores = false);

public sealed class LearningLeaderboardService(AppDbContext db, IMemoryCache cache)
{
    public async Task<LearningLeaderboardResponse> GetAsync(string metric, CancellationToken token)
    {
        var cacheKey = $"learning-leaderboard:{metric}";
        if (cache.TryGetValue(cacheKey, out LearningLeaderboardResponse? cached) && cached is not null)
            return cached;

        var eligibleChallengeIds = await db.SkillTreeRevisions.AsNoTracking()
            .Where(revision => revision.SkillTree.DeletedAtUtc == null && revision.SkillTree.CurrentPublishedRevisionId == revision.Id)
            .SelectMany(revision => revision.Categories)
            .Where(reference => reference.Category.DeletedAtUtc == null)
            .SelectMany(reference => reference.Category.Skills)
            .Where(reference => reference.Skill.DeletedAtUtc == null && reference.Skill.IsEnabled)
            .SelectMany(reference => reference.Skill.Contents)
            .Where(content => content.ChallengeId != null && content.Challenge!.PublicationState == ChallengePublicationState.Published && content.Challenge.IsEnabled)
            .Select(content => content.ChallengeId!.Value).Distinct().ToArrayAsync(token);

        // These old solves are already included in the immutable account baseline.
        // Only new native solves depend on current publication/availability and question values.
        var solves = await db.ChallengeProgress.AsNoTracking()
            .Where(progress => progress.ImportedScoreBaselineId == null && eligibleChallengeIds.Contains(progress.ChallengeId) &&
                               progress.User.UserName != null && progress.User.Role != Role.Banned &&
                               !db.PlatformScoreBaselines.Any(baseline => baseline.UserId == progress.UserId && baseline.Hidden))
            .Select(progress => new Solve(progress.UserId, progress.User.UserName!, progress.SolvedAtUtc, progress.Challenge.Score))
            .ToArrayAsync(token);
        var baselines = await db.PlatformScoreBaselines.AsNoTracking()
            .Where(baseline => !baseline.Hidden && baseline.User.Role != Role.Banned && baseline.User.UserName != null)
            .Select(baseline => new Baseline(baseline.UserId, baseline.User.UserName!, baseline.Score, baseline.SolvedCount,
                baseline.HasActivity, baseline.LastScoringAtUtc, baseline.CreatedAtUtc))
            .ToArrayAsync(token);
        var byUser = solves.ToLookup(solve => solve.UserId);
        var baseByUser = baselines.ToDictionary(baseline => baseline.UserId);
        var ranked = solves.Select(solve => solve.UserId).Concat(baselines.Where(baseline => baseline.HasActivity).Select(baseline => baseline.UserId))
            .Distinct().Select(userId =>
            {
                var native = byUser[userId].ToArray();
                baseByUser.TryGetValue(userId, out var baseline);
                var earnedScore = native.Sum(solve => (long)solve.Score);
                return new Ranked(baseline?.UserName ?? native[0].UserName,
                    checked((baseline?.SolvedCount ?? 0) + native.Length), (baseline?.Score ?? 0) + earnedScore,
                    baseline?.Score ?? 0, earnedScore, baseline?.SolvedCount ?? 0, native.Length,
                    native.Select(solve => solve.SolvedAtUtc).Append(baseline?.LastScoringAtUtc ?? DateTimeOffset.MinValue).Max(), native, baseline);
            });
        var sorted = metric == "solves"
            ? ranked.OrderByDescending(item => item.SolvedCount).ThenByDescending(item => item.Score)
            : ranked.OrderByDescending(item => item.Score).ThenByDescending(item => item.SolvedCount);
        var top = sorted.ThenBy(item => item.LastSolveAtUtc).ThenBy(item => item.UserName, StringComparer.Ordinal).Take(50).ToArray();
        var entries = top.Select((item, index) => new LearningLeaderboardEntry(index + 1, item.UserName, item.SolvedCount,
            item.Score, item.ImportedScore, item.EarnedScore, item.ImportedSolvedCount, item.EarnedSolvedCount)).ToArray();
        var dates = solves.Select(solve => Date(solve.SolvedAtUtc))
            .Concat(baselines.Where(baseline => baseline.HasActivity).Select(baseline => Date(baseline.CreatedAtUtc))).ToArray();
        var endDate = dates.Length == 0 ? (DateOnly?)null : dates.Max();
        var series = top.Take(10).Select(item =>
        {
            var solvedCount = 0;
            long score = 0;
            var points = Events(item).GroupBy(point => point.Date).OrderBy(group => group.Key).Select(group =>
            {
                solvedCount += group.Sum(point => point.SolvedCount);
                score += group.Sum(point => point.Score);
                return new LearningLeaderboardPoint(group.Key, solvedCount, score);
            }).ToList();
            if (endDate is { } end && points.Count > 0 && points[^1].Date < end)
                points.Add(new LearningLeaderboardPoint(end, solvedCount, score));
            return new LearningLeaderboardSeries(item.UserName, points);
        }).ToArray();
        var response = new LearningLeaderboardResponse(metric, DateTimeOffset.UtcNow, entries, series,
            top.Any(item => item.Baseline is { HasActivity: true }));
        cache.Set(cacheKey, response, TimeSpan.FromSeconds(10));
        return response;
    }

    private static IEnumerable<LearningLeaderboardPoint> Events(Ranked item)
    {
        // A snapshot is not a reconstruction of historical daily dynamic scores.
        if (item.Baseline is { HasActivity: true } baseline)
            yield return new LearningLeaderboardPoint(Date(baseline.CreatedAtUtc), baseline.SolvedCount, baseline.Score);
        foreach (var solve in item.Solves)
            yield return new LearningLeaderboardPoint(Date(solve.SolvedAtUtc), 1, solve.Score);
    }
    private static DateOnly Date(DateTimeOffset time) => DateOnly.FromDateTime(time.ToOffset(TimeSpan.FromHours(8)).Date);
    private sealed record Solve(Guid UserId, string UserName, DateTimeOffset SolvedAtUtc, int Score);
    private sealed record Baseline(Guid UserId, string UserName, int Score, int SolvedCount, bool HasActivity,
        DateTimeOffset? LastScoringAtUtc, DateTimeOffset CreatedAtUtc);
    private sealed record Ranked(string UserName, int SolvedCount, long Score, int ImportedScore, long EarnedScore,
        int ImportedSolvedCount, int EarnedSolvedCount, DateTimeOffset LastSolveAtUtc, Solve[] Solves, Baseline? Baseline);
}
