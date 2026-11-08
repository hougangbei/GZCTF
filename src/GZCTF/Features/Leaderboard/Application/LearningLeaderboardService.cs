using GZCTF.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GZCTF.Features.Leaderboard.Application;

public sealed record LearningLeaderboardEntry(int Rank, string UserName, int SolvedCount, long Score,
    int ImportedScore = 0, long EarnedScore = 0, int ImportedSolvedCount = 0, int EarnedSolvedCount = 0, int Level = 1, string Bio = "", string? Avatar = null);
public sealed record LearningLeaderboardPoint(DateOnly Date, int SolvedCount, long Score);
public sealed record LearningLeaderboardSeries(string UserName, IReadOnlyList<LearningLeaderboardPoint> Points);
public sealed record LearningLeaderboardResponse(string Metric, DateTimeOffset GeneratedAtUtc, IReadOnlyList<LearningLeaderboardEntry> Entries,
    IReadOnlyList<LearningLeaderboardSeries> Series, bool HasImportedScores = false, bool HasHistoricalTrend = false);

public sealed class LearningLeaderboardService(AppDbContext db, IMemoryCache cache, ImportedScoreTimelineService? importedTimeline = null,
    RankingScoreService? scoreService = null)
{
    public async Task<LearningLeaderboardResponse> GetAsync(string metric, CancellationToken token)
    {
        // Read one shared projection for public/admin results. Visibility changes take effect immediately.
        var snapshots = (await (scoreService ?? new RankingScoreService(db)).SnapshotAsync(token)).Where(s => s.HasActivity && !s.User.Hidden && s.User.Role != "Banned");
        var sorted = metric == "solves" ? snapshots.OrderByDescending(s => s.User.SolvedCount).ThenByDescending(s => s.User.Score)
            : snapshots.OrderByDescending(s => s.User.Score).ThenByDescending(s => s.User.SolvedCount);
        var top = sorted.ThenBy(s => s.LastScoringAtUtc).ThenBy(s => s.User.UserName, StringComparer.Ordinal).Take(50).ToArray();
        var entries = top.Select((s, i) => new LearningLeaderboardEntry(i + 1, s.User.UserName, s.User.SolvedCount, s.User.Score,
            (int)s.User.ImportedScore, s.User.EarnedScore, s.User.ImportedSolvedCount, s.User.EarnedSolvedCount, s.User.Level, s.User.Bio, s.User.Avatar)).ToArray();
        var ids = top.Take(10).Select(s => s.User.UserId).ToArray();
        var baselines = await db.PlatformScoreBaselines.AsNoTracking().Where(b => ids.Contains(b.UserId) && b.HasActivity).ToArrayAsync(token);
        var history = importedTimeline is null ? new Dictionary<Guid, IReadOnlyList<LearningLeaderboardPoint>>()
            : await importedTimeline.GetAsync(baselines.Select(b => new ImportedTimelineBaseline(b.Id, b.BatchId, b.SourceUserId, b.Score, b.SolvedCount)).ToArray(), token);
        var journal = await db.RankingOperations.AsNoTracking().Where(o => ids.Contains(o.UserId)).ToArrayAsync(token);
        var nativeEvents = await db.RankingScoreEvents.AsNoTracking().Where(e => ids.Contains(e.UserId)).ToArrayAsync(token);
        var corrections = await db.RankingSolveCorrections.AsNoTracking().Where(c => ids.Contains(c.UserId) && c.ImportedScoreBaselineId == null).ToArrayAsync(token);
        var today = Date(DateTimeOffset.UtcNow);
        var series = top.Take(10).Select(s =>
        {
            List<LearningLeaderboardPoint> events = [];
            var baseline = baselines.SingleOrDefault(b => b.UserId == s.User.UserId);
            if (baseline != null)
            {
                if (history.TryGetValue(baseline.Id, out var points))
                    events.AddRange(points);
                else
                    events.Add(new(Date(baseline.CreatedAtUtc), baseline.SolvedCount, baseline.Score));
            }
            var corrected = corrections.Where(c => c.UserId == s.User.UserId).Select(c => c.ChallengeId).ToHashSet();
            events.AddRange(s.Solves.Where(p => !corrected.Contains(p.ChallengeId)).Select(p => new LearningLeaderboardPoint(Date(p.SolvedAtUtc), 1, p.Score)));
            events.AddRange(nativeEvents.Where(e => e.UserId == s.User.UserId).Select(e => new LearningLeaderboardPoint(Date(e.AtUtc), e.SolvedDelta, e.ScoreDelta)));
            events.AddRange(journal.Where(o => o.UserId == s.User.UserId && (o.Delta != 0 || o.SolvedDelta != 0)).Select(o => new LearningLeaderboardPoint(Date(o.CreatedAtUtc), o.SolvedDelta, o.Delta)));
            int solves = 0;
            long score = 0;
            var cumulative = events.GroupBy(p => p.Date).OrderBy(g => g.Key).Select(g => { solves += g.Sum(p => p.SolvedCount); score += g.Sum(p => p.Score); return new LearningLeaderboardPoint(g.Key, solves, score); }).ToList();
            if (baseline != null && history.ContainsKey(baseline.Id) && cumulative.Count > 0 && cumulative[0].Date > DateOnly.MinValue)
                cumulative.Insert(0, new(cumulative[0].Date.AddDays(-1), 0, 0));
            if (cumulative.Count > 0 && cumulative[^1].Date < today)
                cumulative.Add(new(today, solves, score));
            // Current publication and editable native values are authoritative at the end of the trend.
            if (cumulative.Count > 0)
                cumulative[^1] = new(cumulative[^1].Date, s.User.SolvedCount, s.User.Score);
            return new LearningLeaderboardSeries(s.User.UserName, cumulative);
        }).ToArray();
        return new(metric, DateTimeOffset.UtcNow, entries, series, baselines.Length > 0, history.Count > 0);
    }
    private static DateOnly Date(DateTimeOffset time) => DateOnly.FromDateTime(time.ToOffset(TimeSpan.FromHours(8)).Date);
}
