namespace GZCTF.Features.SkillTrees.Application;

/// <summary>
/// Seam for invalidating cached skill tree projections when shared category data changes.
/// A distributed cache can be plugged in later without touching the application services.
/// </summary>
public interface ISkillTreeCacheInvalidator
{
    Task InvalidateTreeAsync(Guid skillTreeId, CancellationToken token);
    Task InvalidateByCategoryAsync(Guid categoryId, CancellationToken token);
}

public sealed class NoopSkillTreeCacheInvalidator : ISkillTreeCacheInvalidator
{
    public Task InvalidateTreeAsync(Guid skillTreeId, CancellationToken token) => Task.CompletedTask;

    public Task InvalidateByCategoryAsync(Guid categoryId, CancellationToken token) => Task.CompletedTask;
}

/// <summary>The discovery graph is read directly; invalidate aggregate caches that depend on its reachability.</summary>
public sealed class SkillTreeCacheInvalidator(Microsoft.Extensions.Caching.Memory.IMemoryCache cache) : ISkillTreeCacheInvalidator
{
    public Task InvalidateTreeAsync(Guid skillTreeId, CancellationToken token) => InvalidateAsync();
    public Task InvalidateByCategoryAsync(Guid categoryId, CancellationToken token) => InvalidateAsync();
    private Task InvalidateAsync()
    {
        cache.Remove("learning-leaderboard:solves");
        cache.Remove("learning-leaderboard:score");
        return Task.CompletedTask;
    }
}
