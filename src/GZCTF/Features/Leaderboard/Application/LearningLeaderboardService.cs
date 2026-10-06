using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GZCTF.Features.Leaderboard.Application;

public sealed record LearningLeaderboardEntry(
    int Rank, string UserName, int SolvedCount, int Score);

public sealed record LearningLeaderboardPoint(DateOnly Date, int SolvedCount, int Score);

public sealed record LearningLeaderboardSeries(
    string UserName, IReadOnlyList<LearningLeaderboardPoint> Points);

public sealed record LearningLeaderboardResponse(
    string Metric,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<LearningLeaderboardEntry> Entries,
    IReadOnlyList<LearningLeaderboardSeries> Series);

public sealed class LearningLeaderboardService(AppDbContext db, IMemoryCache cache)
{
    public async Task<LearningLeaderboardResponse> GetAsync(string metric, CancellationToken token)
    {
        var cacheKey = $"learning-leaderboard:{metric}";
        if (cache.TryGetValue(cacheKey, out LearningLeaderboardResponse? cached) && cached is not null)
            return cached;

        var eligibleChallengeIds = await db.SkillTreeRevisions
            .AsNoTracking()
            .Where(revision => revision.SkillTree.DeletedAtUtc == null &&
                               revision.SkillTree.CurrentPublishedRevisionId == revision.Id)
            .SelectMany(revision => revision.Categories)
            .Where(reference => reference.Category.DeletedAtUtc == null)
            .SelectMany(reference => reference.Category.Contents)
            .Where(content => content.ChallengeId != null &&
                              content.Challenge!.PublicationState == ChallengePublicationState.Published &&
                              content.Challenge.IsEnabled)
            .Select(content => content.ChallengeId!.Value)
            .Distinct()
            .ToArrayAsync(token);

        var solves = await db.ChallengeProgress
            .AsNoTracking()
            .Where(progress => eligibleChallengeIds.Contains(progress.ChallengeId) &&
                               progress.User.UserName != null)
            .Select(progress => new
            {
                progress.UserId,
                UserName = progress.User.UserName!,
                progress.SolvedAtUtc,
                progress.Challenge.Score
            })
            .ToArrayAsync(token);

        var ranked = solves
            .GroupBy(solve => solve.UserId)
            .Select(group => new
            {
                UserName = group.First().UserName,
                SolvedCount = group.Count(),
                Score = group.Sum(solve => solve.Score),
                LastSolveAtUtc = group.Max(solve => solve.SolvedAtUtc),
                Solves = group.ToArray()
            });

        var sorted = metric == "solves"
            ? ranked.OrderByDescending(item => item.SolvedCount).ThenByDescending(item => item.Score)
            : ranked.OrderByDescending(item => item.Score).ThenByDescending(item => item.SolvedCount);
        var top = sorted
            .ThenBy(item => item.LastSolveAtUtc)
            .ThenBy(item => item.UserName, StringComparer.Ordinal)
            .Take(50)
            .ToArray();

        var entries = top.Select((item, index) => new LearningLeaderboardEntry(
            index + 1, item.UserName, item.SolvedCount, item.Score)).ToArray();
        var latestDate = solves.Length == 0
            ? (DateOnly?)null
            : solves.Max(solve => DateOnly.FromDateTime(solve.SolvedAtUtc.ToOffset(TimeSpan.FromHours(8)).Date));
        var series = top.Take(10).Select(item =>
        {
            var solvedCount = 0;
            var score = 0;
            var points = item.Solves
                .GroupBy(solve => DateOnly.FromDateTime(solve.SolvedAtUtc.ToOffset(TimeSpan.FromHours(8)).Date))
                .OrderBy(group => group.Key)
                .Select(group =>
                {
                    solvedCount += group.Count();
                    score += group.Sum(solve => solve.Score);
                    return new LearningLeaderboardPoint(group.Key, solvedCount, score);
                })
                .ToList();
            if (latestDate is { } endDate && points.Count > 0 && points[^1].Date < endDate)
                points.Add(new LearningLeaderboardPoint(endDate, solvedCount, score));
            return new LearningLeaderboardSeries(item.UserName, points);
        }).ToArray();

        var response = new LearningLeaderboardResponse(metric, DateTimeOffset.UtcNow, entries, series);
        cache.Set(cacheKey, response, TimeSpan.FromSeconds(10));
        return response;
    }
}
