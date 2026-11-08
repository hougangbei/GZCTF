using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Storage.Interface;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace GZCTF.Features.SkillTrees.Application;

/// <summary>Tracks private uploads so abandoned files can be reclaimed after a grace period.</summary>
public sealed class CourseAssetRegistry(IServiceScopeFactory scopes, IBlobStorage storage)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    public async Task StoreAsync(string key, string referenceToken, Stream input, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await LockAsync(db, key, token);
        var created = !await storage.ExistsAsync(key, token);
        try
        {
            if (created) await storage.WriteAsync(key, input, cancellationToken: token);
            var row = await db.CourseStoredAssets.SingleOrDefaultAsync(a => a.StorageKey == key, token);
            if (row is null) db.CourseStoredAssets.Add(new CourseStoredAsset { StorageKey = key, ReferenceToken = referenceToken, LastTouchedUtc = DateTimeOffset.UtcNow });
            else { row.ReferenceToken = referenceToken; row.LastTouchedUtc = DateTimeOffset.UtcNow; }
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        }
        catch
        {
            if (created) await storage.DeleteAsync(key, CancellationToken.None);
            throw;
        }
    }
    internal static Task LockAsync(AppDbContext db, string key, CancellationToken token) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", token);

    /// <summary>Serialize reference writes against cleanup and reject already reclaimed files.</summary>
    public async Task LockReferencedAssetsAsync(AppDbContext db, string text, CancellationToken token)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text, @"/api/(?:admin/skill-trees/(?<tree>[0-9a-fA-F-]{36})/workspace/assets|course-assets/(?<tree>[0-9a-fA-F-]{36})/[0-9a-fA-F-]{36})/(?<hash>[0-9a-f]{64})/", RegexOptions.CultureInvariant))
            if (Guid.TryParse(m.Groups["tree"].Value, out var tree)) keys.Add($"course-assets/{tree:N}/{m.Groups["hash"].Value}");
        foreach (Match m in Regex.Matches(text, @"/api/(?:admin/)?content-assets/(?<kind>lesson|challenge)/(?<id>[0-9a-fA-F-]{36})/(?<hash>[0-9a-f]{64})/", RegexOptions.CultureInvariant))
            if (Guid.TryParse(m.Groups["id"].Value, out var id)) keys.Add($"content-assets/{m.Groups["kind"].Value}/{id:N}/{m.Groups["hash"].Value}");
        foreach (Match m in Regex.Matches(text, @"(?:course-assets/[0-9a-f]{32}|content-assets/(?:lesson|challenge)/[0-9a-f]{32})/[0-9a-f]{64}", RegexOptions.CultureInvariant)) keys.Add(m.Value);
        foreach (var key in keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            await LockAsync(db, key, token);
            if (!await storage.ExistsAsync(key, token)) throw new CourseAssetUnavailableException();
        }
    }

    public async Task<int> CollectAsync(DateTimeOffset now, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = now - Retention;
        var keys = await db.CourseStoredAssets.AsNoTracking().Where(a => a.LastTouchedUtc < cutoff).OrderBy(a => a.LastTouchedUtc)
            .Select(a => a.StorageKey).Take(100).ToArrayAsync(token);
        var removed = 0;
        foreach (var key in keys)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            await LockAsync(db, key, token);
            var row = await db.CourseStoredAssets.SingleOrDefaultAsync(a => a.StorageKey == key, token);
            if (row is null || row.LastTouchedUtc >= cutoff) { await transaction.CommitAsync(token); continue; }
            if (await IsReferencedAsync(db, row, token))
            {
                row.LastTouchedUtc = now;
                await db.SaveChangesAsync(token); await transaction.CommitAsync(token); continue;
            }
            await storage.DeleteAsync(key, token);
            db.CourseStoredAssets.Remove(row);
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token); removed++;
        }
        return removed;
    }
    private static async Task<bool> IsReferencedAsync(AppDbContext db, CourseStoredAsset asset, CancellationToken token)
    {
        var value = asset.ReferenceToken;
        if (await db.CourseAuthoringWorkspaces.AnyAsync(w => w.DocumentJson.Contains(value) || w.CoverStorageKey == asset.StorageKey, token) ||
            await db.CourseWorkspaceContents.AnyAsync(c => c.PayloadJson.Contains(value), token) ||
            await db.CoursePublishedVersions.AnyAsync(v => v.PublicDocumentJson.Contains(value) || v.CoverStorageKey == asset.StorageKey, token) ||
            await db.CoursePublishedContents.AnyAsync(c => c.PayloadJson.Contains(value), token) ||
            await db.UserChallengeInstances.AnyAsync(i => (i.PublishedChallengeJson != null && i.PublishedChallengeJson.Contains(value)) ||
                (i.AssignedAttachmentKey != null && i.AssignedAttachmentKey.Contains(value)), token) ||
            await db.LessonLocalizations.AnyAsync(l => l.Body.Contains(value), token) ||
            await db.ChallengeLocalizations.AnyAsync(c => c.Body.Contains(value), token) ||
            await db.ChallengeHints.AnyAsync(h => h.Content.Contains(value), token) ||
            await db.ChallengeWriteups.AnyAsync(w => w.Content.Contains(value), token)) return true;
        // JSONB fields are checked in memory to preserve PostgreSQL JSON column semantics.
        var runtime = await db.Challenges.AsNoTracking().Select(c => c.RuntimeConfigurationJson).ToArrayAsync(token);
        var flags = await db.ChallengeFlags.AsNoTracking().Select(f => new { f.AttachmentPoolKey, f.MetadataJson }).ToArrayAsync(token);
        var instances = await db.UserChallengeInstances.AsNoTracking().Select(i => i.RuntimeMetadataJson).ToArrayAsync(token);
        return runtime.Any(s => s?.Contains(value, StringComparison.Ordinal) == true) ||
            flags.Any(f => f.AttachmentPoolKey?.Contains(value, StringComparison.Ordinal) == true || f.MetadataJson?.Contains(value, StringComparison.Ordinal) == true) ||
            instances.Any(s => s?.Contains(value, StringComparison.Ordinal) == true);
    }
}

public sealed class CourseAssetCleanupService(IServiceScopeFactory scopes, ILogger<CourseAssetCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<CourseAssetRegistry>().CollectAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning(ex, "Private course asset cleanup failed; it will be retried."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public sealed class CourseAssetUnavailableException : Exception;
