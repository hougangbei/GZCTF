using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Features.Imports.Domain;
using GZCTF.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GZCTF.Features.Leaderboard.Application;

internal sealed record ImportedTimelineBaseline(Guid Id, Guid BatchId, string SourceUserId, int Score, int SolvedCount);

// A read-only approximation using immutable source values, never today's editable question values.
public sealed class ImportedScoreTimelineService(AppDbContext db, IMemoryCache cache, IDataProtectionProvider protection)
{
    internal async Task<IReadOnlyDictionary<Guid, IReadOnlyList<LearningLeaderboardPoint>>> GetAsync(
        IReadOnlyList<ImportedTimelineBaseline> selected, CancellationToken token)
    {
        Dictionary<Guid, IReadOnlyList<LearningLeaderboardPoint>> result = [];
        if (selected.Count == 0) return result;
        var maps = new Dictionary<Guid, IReadOnlyDictionary<string, int>>();
        var missing = new List<Guid>();
        foreach (var batchId in selected.Select(b => b.BatchId).Distinct())
            if (cache.TryGetValue($"imported-timeline-values:{batchId}", out QuestionValues? cached) && cached is not null)
                maps[batchId] = cached.Values;
            else missing.Add(batchId);
        if (missing.Count > 0)
        {
            var batches = await db.PlatformImportBatches.AsNoTracking().Where(b => missing.Contains(b.Id))
                .Select(b => new { b.Id, b.ProtectedMetadata }).ToArrayAsync(token);
            foreach (var batch in batches)
            {
                var values = ReadValues(batch.ProtectedMetadata);
                if (values is null) continue;
                maps[batch.Id] = values;
                // Cache only the safe projection, never decrypted source metadata or submitted flags.
                cache.Set($"imported-timeline-values:{batch.Id}", new QuestionValues(values), TimeSpan.FromMinutes(10));
            }
        }
        var candidates = selected.Where(b => maps.ContainsKey(b.BatchId)).ToArray();
        if (candidates.Length == 0) return result;
        var submissions = await db.PlatformHistorySubmissions.AsNoTracking()
            .Where(Selected<PlatformHistorySubmission>(candidates)).Where(s => s.Result == "correct")
            .Select(s => new { s.BatchId, s.SourceUserId, s.SourceChallengeId, s.SubmittedAtUtc, s.SourceTeamId }).ToArrayAsync(token);
        var awards = await db.PlatformHistoryAwards.AsNoTracking().Where(Selected<PlatformHistoryAward>(candidates))
            .Select(a => new { a.BatchId, a.SourceUserId, a.Value, a.AwardedAtUtc, a.SourceTeamId }).ToArrayAsync(token);
        var solvesByUser = submissions.ToLookup(s => (s.BatchId, s.SourceUserId));
        var awardsByUser = awards.ToLookup(a => (a.BatchId, a.SourceUserId));
        foreach (var baseline in candidates)
        {
            var solves = solvesByUser[(baseline.BatchId, baseline.SourceUserId)].ToArray();
            var adjustments = awardsByUser[(baseline.BatchId, baseline.SourceUserId)].ToArray();
            var values = maps[baseline.BatchId];
            if (solves.Any(s => s.SubmittedAtUtc is null || s.SourceTeamId is not null || !values.ContainsKey(s.SourceChallengeId)) ||
                adjustments.Any(a => a.AwardedAtUtc is null || a.SourceTeamId is not null)) continue;
            var events = solves.GroupBy(s => s.SourceChallengeId, StringComparer.Ordinal)
                .Select(g => new LearningLeaderboardPoint(Date(g.Min(s => s.SubmittedAtUtc!.Value)), 1, values[g.Key]))
                .Concat(adjustments.Select(a => new LearningLeaderboardPoint(Date(a.AwardedAtUtc!.Value), 0, a.Value))).ToArray();
            if (events.Length == 0 || events.Sum(e => e.Score) != baseline.Score || events.Sum(e => e.SolvedCount) != baseline.SolvedCount) continue;
            result[baseline.Id] = events;
        }
        return result;
    }

    private sealed record FrozenContext(IReadOnlyDictionary<string, int> Values,
        IReadOnlyDictionary<string, string> SubmissionChallenges, ILookup<Guid, string> MappedChallenges);
    private readonly Dictionary<Guid, FrozenContext> _frozenContexts = [];

    internal async Task<int> FrozenScoreAsync(GZCTF.Features.LearningProgress.Domain.ChallengeProgress progress, CancellationToken token)
    {
        var baselineId = progress.ImportedScoreBaselineId!.Value;
        if (!_frozenContexts.TryGetValue(baselineId, out var context))
        {
            var baseline = await db.PlatformScoreBaselines.AsNoTracking().SingleAsync(b => b.Id == baselineId, token);
            IReadOnlyDictionary<string, int>? values;
            if (cache.TryGetValue($"imported-timeline-values:{baseline.BatchId}", out QuestionValues? cached) && cached is not null)
                values = cached.Values;
            else
            {
                var batch = await db.PlatformImportBatches.AsNoTracking().SingleAsync(b => b.Id == baseline.BatchId, token);
                values = ReadValues(batch.ProtectedMetadata);
                if (values is not null)
                    cache.Set($"imported-timeline-values:{baseline.BatchId}", new QuestionValues(values), TimeSpan.FromMinutes(10));
            }
            if (values is null) throw new RankingValidationException("ranking.import_frozen_score_unavailable");
            var submissions = await db.PlatformHistorySubmissions.AsNoTracking()
                .Where(h => h.BatchId == baseline.BatchId && h.SourceUserId == baseline.SourceUserId)
                .Select(h => new { h.SourceId, h.SourceChallengeId }).ToArrayAsync(token);
            var mappings = await db.PlatformImportMaps.AsNoTracking().Where(m => m.SourceInstance == baseline.SourceInstance && m.Kind == "challenge")
                .Select(m => new { m.TargetId, m.SourceId }).ToArrayAsync(token);
            context = new(values, submissions.ToDictionary(h => h.SourceId, h => h.SourceChallengeId), mappings.ToLookup(m => m.TargetId, m => m.SourceId));
            _frozenContexts[baselineId] = context;
        }
        string? sourceChallengeId = null;
        if (progress.AttributionMetadataJson is not null)
        {
            using var metadata = JsonDocument.Parse(progress.AttributionMetadataJson);
            if (metadata.RootElement.TryGetProperty("SourceId", out var id) && id.ValueKind == JsonValueKind.String)
                sourceChallengeId = context.SubmissionChallenges.GetValueOrDefault(id.GetString()!);
        }
        sourceChallengeId ??= context.MappedChallenges[progress.ChallengeId].SingleOrDefault();
        if (sourceChallengeId is null || !context.Values.TryGetValue(sourceChallengeId, out var score))
            throw new RankingValidationException("ranking.import_frozen_score_unavailable");
        return score;
    }

    // Exact selected batch/user pairs keep unrelated accounts out of the history reads.
    private static Expression<Func<T, bool>> Selected<T>(IEnumerable<ImportedTimelineBaseline> selected)
    {
        var row = Expression.Parameter(typeof(T), "row");
        Expression match = Expression.Constant(false);
        foreach (var baseline in selected)
            match = Expression.OrElse(match, Expression.AndAlso(
                Expression.Equal(Expression.Property(row, "BatchId"), Expression.Constant(baseline.BatchId)),
                Expression.Equal(Expression.Property(row, "SourceUserId"), Expression.Constant(baseline.SourceUserId))));
        return Expression.Lambda<Func<T, bool>>(match, row);
    }

    private IReadOnlyDictionary<string, int>? ReadValues(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return null;
        try
        {
            using var json = JsonDocument.Parse(protection.CreateProtector("GZCTF.PlatformImport.History.v1").Unprotect(metadata));
            if (!json.RootElement.TryGetProperty("Source", out var source) || source.ValueKind != JsonValueKind.Object ||
                !source.TryGetProperty("challenges", out var challenges) || challenges.ValueKind != JsonValueKind.Array) return null;
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var challenge in challenges.EnumerateArray())
            {
                if (challenge.ValueKind != JsonValueKind.Object || !challenge.TryGetProperty("id", out var id) ||
                    id.ValueKind is not (JsonValueKind.Number or JsonValueKind.String) || string.IsNullOrWhiteSpace(id.ToString()) ||
                    !challenge.TryGetProperty("value", out var value) || !int.TryParse(value.ToString(), out var score) ||
                    !values.TryAdd(id.ToString(), score)) return null;
            }
            return values;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
    private static DateOnly Date(DateTimeOffset time) => DateOnly.FromDateTime(time.ToOffset(TimeSpan.FromHours(8)).Date);
    private sealed record QuestionValues(IReadOnlyDictionary<string, int> Values);
}
