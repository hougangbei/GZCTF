using System.Text.Json;
using System.Collections.Concurrent;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.ChallengeRuntime.Domain;
using GZCTF.Features.ChallengeRuntime.Infrastructure;
using GZCTF.Models;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using CanonicalChallenge = GZCTF.Features.ChallengeLibrary.Domain.Challenge;

namespace GZCTF.Features.ChallengeRuntime.Application;

public sealed record ChallengeInstanceResponse(
    Guid Id,
    ChallengeInstanceStatus Status,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string? PublicIp,
    int? PublicPort,
    string? AttachmentFileName,
    string? AttachmentSha256);

public sealed class ChallengeRuntimeService(
    AppDbContext db,
    ILegacyContainerRuntimeAdapter containers,
    LearningInstanceSettingsService settingsService)
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> StartLocks = new();
    public async Task<UserChallengeInstance> GetOrCreateInstanceAsync(
        Guid userId, Guid challengeId, CancellationToken token = default)
    {
        var current = await db.UserChallengeInstances
            .SingleOrDefaultAsync(item => item.UserId == userId && item.ChallengeId == challengeId && item.IsActive,
                token);
        if (current is not null)
            return current;

        var instance = new UserChallengeInstance
        {
            UserId = userId,
            ChallengeId = challengeId,
            Status = ChallengeInstanceStatus.Pending,
            IsActive = true
        };
        db.UserChallengeInstances.Add(instance);
        try
        {
            await db.SaveChangesAsync(token);
            return instance;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return await db.UserChallengeInstances.SingleAsync(item =>
                item.UserId == userId && item.ChallengeId == challengeId && item.IsActive, token);
        }
    }

    public async Task<UserChallengeInstance> StartAsync(
        Guid userId, Guid challengeId, CancellationToken token = default)
    {
        var gate = StartLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            var current = await db.UserChallengeInstances.SingleOrDefaultAsync(item =>
                item.UserId == userId && item.ChallengeId == challengeId && item.IsActive, token);
            var now = DateTimeOffset.UtcNow;
            if (current?.Status == ChallengeInstanceStatus.Running)
            {
                var containerRunning = current.ContainerId is { } containerId &&
                    await db.Containers.AnyAsync(item => item.Id == containerId &&
                        item.Status == ContainerStatus.Running && item.ExpectStopAt > now, token);
                if (current.ExpiresAtUtc > now && containerRunning) return current;
                await RetireExpiredAsync(current, token);
                current = null;
            }

            var challenge = await db.Challenges.Include(item => item.Flags)
                .SingleAsync(item => item.Id == challengeId, token);
            if (!challenge.Type.IsContainer())
                throw new InvalidOperationException("challenge.container_required");

            var policy = await settingsService.GetAsync(token);
            var running = await db.UserChallengeInstances.CountAsync(item =>
                item.UserId == userId && item.IsActive &&
                item.Status == ChallengeInstanceStatus.Running && item.ExpiresAtUtc > now &&
                db.Containers.Any(container => container.Id == item.ContainerId &&
                    container.Status == ContainerStatus.Running && container.ExpectStopAt > now), token);
            if (running >= policy.MaxConcurrentPerUser)
                throw new ChallengeInstanceLimitException();

            var instance = current ?? await GetOrCreateInstanceAsync(userId, challengeId, token);

            var settings = ReadContainerSettings(challenge.RuntimeConfigurationJson);
            var image = settings.ContainerImage ?? settings.Image;
            if (string.IsNullOrWhiteSpace(image))
                throw new InvalidOperationException("challenge.container_configuration_invalid");
            var flag = ResolveFlag(challenge, userId, settings.FlagTemplate);
            var container = await containers.StartAsync(new CanonicalContainerRequest(
                userId, challengeId, image, settings.ExposedPort, settings.Cpu,
                settings.MemoryMb, settings.StorageMb, settings.NetworkMode, flag), token);
            if (container is null)
                throw new InvalidOperationException("challenge.container_unavailable");

            var expires = DateTimeOffset.UtcNow.AddMinutes(policy.LifetimeMinutes);
            if (db.Entry(container).State == EntityState.Detached) db.Containers.Add(container);
            container.ExpectStopAt = expires;
            instance.ContainerId = container.Id;
            instance.AssignedFlag = flag;
            instance.Status = ChallengeInstanceStatus.Running;
            instance.StartedAtUtc ??= DateTimeOffset.UtcNow;
            instance.ExpiresAtUtc = expires;
            await db.SaveChangesAsync(token);
            return instance;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<UserChallengeInstance> ExtendAsync(
        Guid userId, Guid challengeId, CancellationToken token = default)
    {
        var instance = await GetCurrentInstanceAsync(userId, challengeId, token);
        if (instance.Status != ChallengeInstanceStatus.Running)
            return instance;
        var policy = await settingsService.GetAsync(token);
        var expires = DateTimeOffset.UtcNow.AddMinutes(policy.LifetimeMinutes);
        instance.ExpiresAtUtc = expires;
        if (instance.ContainerId is { } containerId)
        {
            var container = await db.Containers.SingleOrDefaultAsync(item => item.Id == containerId, token);
            if (container is not null) container.ExpectStopAt = expires;
        }
        await db.SaveChangesAsync(token);
        return instance;
    }

    public async Task StopAsync(Guid userId, Guid challengeId, CancellationToken token = default)
    {
        var instance = await db.UserChallengeInstances.SingleOrDefaultAsync(item =>
            item.UserId == userId && item.ChallengeId == challengeId && item.IsActive, token);
        if (instance is null || instance.Status == ChallengeInstanceStatus.Stopped)
            return;
        if (instance.ContainerId is Guid containerId)
        {
            var container = await db.Containers.SingleOrDefaultAsync(item => item.Id == containerId, token);
            if (container is not null)
                await containers.StopAsync(container, token);
        }

        instance.Status = ChallengeInstanceStatus.Stopped;
        instance.IsActive = false;
        instance.StoppedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
    }

    public async Task<UserChallengeInstance> GetOwnedInstanceAsync(
        Guid userId, Guid challengeId, CancellationToken token = default) =>
        await db.UserChallengeInstances.SingleAsync(item =>
            item.UserId == userId && item.ChallengeId == challengeId && item.IsActive, token);

    public async Task<UserChallengeInstance> GetCurrentInstanceAsync(
        Guid userId, Guid challengeId, CancellationToken token = default)
    {
        var instance = await GetOwnedInstanceAsync(userId, challengeId, token);
        if (instance.Status != ChallengeInstanceStatus.Running) return instance;
        var now = DateTimeOffset.UtcNow;
        var running = instance.ExpiresAtUtc > now && instance.ContainerId is { } id &&
            await db.Containers.AnyAsync(container => container.Id == id &&
                container.Status == ContainerStatus.Running && container.ExpectStopAt > now, token);
        if (running) return instance;
        await RetireExpiredAsync(instance, token);
        throw new InvalidOperationException("challenge.instance_expired");
    }

    private async Task RetireExpiredAsync(UserChallengeInstance instance, CancellationToken token)
    {
        if (instance.ContainerId is { } id)
        {
            var container = await db.Containers.SingleOrDefaultAsync(item => item.Id == id, token);
            if (container?.Status == ContainerStatus.Running)
                await containers.StopAsync(container, token);
        }
        instance.Status = ChallengeInstanceStatus.Expired;
        instance.IsActive = false;
        instance.StoppedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
    }

    private static string? ResolveFlag(CanonicalChallenge challenge, Guid userId, string? template)
    {
        var staticFlag = challenge.Flags.FirstOrDefault(flag => flag.Kind == ChallengeFlagKind.Static)?.Value;
        if (challenge.Type == ChallengeType.StaticContainer)
            return staticFlag;
        var actualTemplate = template ?? challenge.Flags.FirstOrDefault(flag =>
            flag.Kind == ChallengeFlagKind.Template)?.Template;
        return (actualTemplate ?? string.Empty).Replace("{userId}", userId.ToString("N"), StringComparison.Ordinal);
    }

    private static ContainerSettings ReadContainerSettings(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            throw new InvalidOperationException("challenge.container_configuration_missing");
        return JsonSerializer.Deserialize<ContainerSettings>(configuration,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidOperationException("challenge.container_configuration_invalid");
    }

    private sealed record ContainerSettings(
        string? ContainerImage,
        string? Image,
        int ExposedPort,
        int Cpu,
        int MemoryMb,
        int StorageMb,
        string NetworkMode,
        string? FlagTemplate);
}
