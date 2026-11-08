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
    private static readonly TimeSpan TestLease = TimeSpan.FromSeconds(90);
    public async Task<UserChallengeInstance> GetOrCreateInstanceAsync(
        Guid userId, Guid challengeId, CancellationToken token = default, bool allowDraft = false, Guid? testSessionId = null)
    {
        var current = await db.UserChallengeInstances
            .SingleOrDefaultAsync(item => item.UserId == userId && item.ChallengeId == challengeId && item.IsActive,
                token);
        if (current is not null)
            return current;

        var publishedChallenge = await PublishedCourseContentReader.GetChallengeAsync(db, challengeId, token, allowDraft)
            ?? throw new InvalidOperationException("challenge.not_published");
        var instance = new UserChallengeInstance
        {
            UserId = userId,
            ChallengeId = challengeId,
            Status = ChallengeInstanceStatus.Pending,
            IsActive = true,
            PublishedChallengeJson = PublishedCourseContentReader.FreezeChallenge(publishedChallenge),
            RuntimeMetadataJson = allowDraft ? JsonSerializer.Serialize(new { adminTest = true, testSessionId }) : null,
            ExpiresAtUtc = testSessionId.HasValue ? DateTimeOffset.UtcNow.Add(TestLease) : null
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
        Guid userId, Guid challengeId, CancellationToken token = default, bool allowDraft = false,
        CanonicalChallenge? draftOverride = null, Guid? testSessionId = null)
    {
        if (testSessionId == Guid.Empty || (testSessionId.HasValue && !allowDraft))
            throw new ChallengeRuntimeException("challenge.test_session_invalid", StatusCodes.Status400BadRequest,
                "A test session requires an administrator and a non-empty session ID.");
        var gate = StartLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            // A disconnected editor must still finish persisting the short lease after startup.
            // Cancellation before acquiring the lock remains safe because no container exists yet.
            if (testSessionId.HasValue) token = CancellationToken.None;
            var challenge = draftOverride ?? await PublishedCourseContentReader.GetChallengeAsync(db, challengeId, token, allowDraft)
                ?? throw new InvalidOperationException("challenge.not_published");
            var current = await db.UserChallengeInstances.SingleOrDefaultAsync(item =>
                item.UserId == userId && item.ChallengeId == challengeId && item.IsActive, token);
            var now = DateTimeOffset.UtcNow;
            if (current?.Status == ChallengeInstanceStatus.Running)
            {
                var containerRunning = current.ContainerId is { } containerId &&
                    await db.Containers.AnyAsync(item => item.Id == containerId &&
                        item.Status == ContainerStatus.Running && item.ExpectStopAt > now, token);
                if (current.ExpiresAtUtc > now && containerRunning)
                {
                    if (allowDraft || draftOverride is not null)
                        EnsureTestOwnership(current, testSessionId);
                    if ((allowDraft || draftOverride is not null) && current.PublishedChallengeJson != PublishedCourseContentReader.FreezeChallenge(challenge))
                        throw new ChallengeRuntimeException("challenge.instance_configuration_changed", StatusCodes.Status409Conflict,
                            "Stop your running test instance before starting the updated draft configuration.");
                    return current;
                }
                await RetireExpiredAsync(current, token);
                current = null;
            }

            if (current is not null && ReadTestSession(current) is not null && current.ExpiresAtUtc <= now)
            {
                await RetireExpiredAsync(current, token);
                current = null;
            }
            if (current is not null && (allowDraft || draftOverride is not null))
                EnsureTestOwnership(current, testSessionId);

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

            var settings = ReadContainerSettings(challenge.RuntimeConfigurationJson);
            var image = settings.ContainerImage ?? settings.Image;
            if (string.IsNullOrWhiteSpace(image))
                throw new InvalidOperationException("challenge.container_configuration_invalid");
            var whale = IsWhale(challenge, settings.FlagMode);
            var flag = ResolveFlag(challenge, userId, settings, whale);
            // Reserve the session before external startup. A concurrent pending learner allocation
            // must never be silently adopted by an editor, including after a uniqueness race.
            var instance = current ?? await GetOrCreateInstanceAsync(userId, challengeId, token,
                allowDraft || draftOverride is not null, testSessionId);
            if (allowDraft || draftOverride is not null) EnsureTestOwnership(instance, testSessionId);
            var container = await containers.StartAsync(new CanonicalContainerRequest(
                userId, challengeId, image, settings.ExposedPort, settings.Cpu,
                settings.MemoryMb, settings.StorageMb, settings.NetworkMode, flag, whale), token);
            if (container is null)
                throw new InvalidOperationException("challenge.container_unavailable");

            var expires = testSessionId.HasValue ? DateTimeOffset.UtcNow.Add(TestLease) : DateTimeOffset.UtcNow.AddMinutes(policy.LifetimeMinutes);
            if (db.Entry(container).State == EntityState.Detached)
                db.Containers.Add(container);
            container.ExpectStopAt = expires;
            instance.ContainerId = container.Id;
            instance.AssignedFlag = flag;
            instance.PublishedChallengeJson = PublishedCourseContentReader.FreezeChallenge(challenge);
            instance.RuntimeMetadataJson = allowDraft || draftOverride is not null
                ? JsonSerializer.Serialize(new { adminTest = true, testSessionId }) : null;
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
        var gate = StartLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            var instance = await GetCurrentInstanceAsync(userId, challengeId, token);
            if (instance.Status != ChallengeInstanceStatus.Running)
                return instance;
            var policy = await settingsService.GetAsync(token);
            var expires = ReadTestSession(instance) is not null
                ? DateTimeOffset.UtcNow.Add(TestLease)
                : DateTimeOffset.UtcNow.AddMinutes(policy.LifetimeMinutes);
            await SetExpiryAsync(instance, expires, token);
            return instance;
        }
        finally { gate.Release(); }
    }

    public async Task StopAsync(Guid userId, Guid challengeId, CancellationToken token = default)
    {
        var gate = StartLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            var instance = await db.UserChallengeInstances.SingleOrDefaultAsync(item =>
                item.UserId == userId && item.ChallengeId == challengeId && item.IsActive, token);
            if (instance is not null) await StopInstanceAsync(instance, token);
        }
        finally { gate.Release(); }
    }

    public async Task<UserChallengeInstance> HeartbeatTestAsync(Guid userId, Guid challengeId,
        Guid instanceId, Guid testSessionId, CancellationToken token = default)
    {
        var gate = StartLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            var instance = await FindOwnedTestAsync(userId, challengeId, instanceId, testSessionId, token);
            var now = DateTimeOffset.UtcNow;
            if (instance is null || instance.ExpiresAtUtc is not { } leaseExpiry || leaseExpiry <= now || instance.ContainerId is not { } id ||
                !await db.Containers.AnyAsync(c => c.Id == id && c.Status == ContainerStatus.Running && c.ExpectStopAt > now, token))
                throw new ChallengeRuntimeException("challenge.instance_not_found", StatusCodes.Status404NotFound,
                    "The test instance is no longer active.");
            await SetExpiryAsync(instance, now.Add(TestLease), token);
            return instance;
        }
        finally { gate.Release(); }
    }

    public async Task StopTestAsync(Guid userId, Guid challengeId, Guid instanceId,
        Guid testSessionId, CancellationToken token = default)
    {
        var gate = StartLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            var instance = await FindOwnedTestAsync(userId, challengeId, instanceId, testSessionId, token);
            if (instance is not null) await StopInstanceAsync(instance, token);
        }
        finally { gate.Release(); }
    }

    private async Task<UserChallengeInstance?> FindOwnedTestAsync(Guid userId, Guid challengeId,
        Guid instanceId, Guid testSessionId, CancellationToken token)
    {
        if (testSessionId == Guid.Empty) return null;
        var instance = await db.UserChallengeInstances.SingleOrDefaultAsync(i => i.Id == instanceId &&
            i.UserId == userId && i.ChallengeId == challengeId && i.IsActive && i.Status == ChallengeInstanceStatus.Running, token);
        return instance is not null && ReadTestSession(instance) == testSessionId ? instance : null;
    }

    private async Task SetExpiryAsync(UserChallengeInstance instance, DateTimeOffset expires, CancellationToken token)
    {
        instance.ExpiresAtUtc = expires;
        if (instance.ContainerId is { } id && await db.Containers.SingleOrDefaultAsync(c => c.Id == id, token) is { } container)
            container.ExpectStopAt = expires;
        await db.SaveChangesAsync(token);
    }

    private async Task StopInstanceAsync(UserChallengeInstance instance, CancellationToken token)
    {
        if (instance.ContainerId is { } id && await db.Containers.SingleOrDefaultAsync(c => c.Id == id, token) is { } container)
            await containers.StopAsync(container, token);
        instance.Status = ChallengeInstanceStatus.Stopped;
        instance.IsActive = false;
        instance.StoppedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
    }

    private static void EnsureTestOwnership(UserChallengeInstance instance, Guid? session)
    {
        if (!IsAdminTest(instance) || ReadTestSession(instance) != session)
            throw new ChallengeRuntimeException("challenge.test_session_conflict", StatusCodes.Status409Conflict,
                "An existing student instance or another editor test is running. Stop it before starting this test.");
    }

    private static bool IsAdminTest(UserChallengeInstance instance)
    {
        try
        {
            using var json = JsonDocument.Parse(instance.RuntimeMetadataJson ?? "{}");
            return json.RootElement.TryGetProperty("adminTest", out var admin) && admin.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return false; }
    }

    private static Guid? ReadTestSession(UserChallengeInstance instance)
    {
        if (!IsAdminTest(instance)) return null;
        using var json = JsonDocument.Parse(instance.RuntimeMetadataJson!);
        return json.RootElement.TryGetProperty("testSessionId", out var session) && session.ValueKind == JsonValueKind.String &&
            session.TryGetGuid(out var id) ? id : null;
    }

    public async Task<UserChallengeInstance> GetOwnedInstanceAsync(
        Guid userId, Guid challengeId, CancellationToken token = default) =>
        await db.UserChallengeInstances.SingleAsync(item =>
            item.UserId == userId && item.ChallengeId == challengeId && item.IsActive, token);

    public async Task<UserChallengeInstance> GetCurrentInstanceAsync(
        Guid userId, Guid challengeId, CancellationToken token = default)
    {
        var instance = await GetOwnedInstanceAsync(userId, challengeId, token);
        if (instance.Status != ChallengeInstanceStatus.Running)
            return instance;
        var now = DateTimeOffset.UtcNow;
        var running = instance.ExpiresAtUtc > now && instance.ContainerId is { } id &&
            await db.Containers.AnyAsync(container => container.Id == id &&
                container.Status == ContainerStatus.Running && container.ExpectStopAt > now, token);
        if (running)
            return instance;
        await RetireExpiredAsync(instance, token, stopContainer: false);
        throw new InvalidOperationException("challenge.instance_expired");
    }

    private async Task RetireExpiredAsync(UserChallengeInstance instance, CancellationToken token,
        bool stopContainer = true)
    {
        // Reads reconcile database state only. The container expiry cron handles remote cleanup.
        if (stopContainer && instance.ContainerId is { } id)
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

    internal static bool IsWhale(CanonicalChallenge challenge, string? mode) =>
        mode == "ctfd-whale" || challenge.SourceType.Equals("CTFd", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveFlag(CanonicalChallenge challenge, Guid userId, ContainerSettings settings, bool whale)
    {
        if (challenge.Type == ChallengeType.StaticContainer && !whale)
            return challenge.Flags.FirstOrDefault(flag => flag.Kind == ChallengeFlagKind.Static)?.Value;
        // Whale static flags are judged against the imported originals, with an independent random runtime Flag.
        var template = challenge.Type == ChallengeType.StaticContainer ? null :
            !string.IsNullOrWhiteSpace(settings.FlagTemplate) ? settings.FlagTemplate :
            challenge.Flags.FirstOrDefault(flag => flag.Kind == ChallengeFlagKind.Template)?.Template;
        if (template?.Contains("{userId}", StringComparison.OrdinalIgnoreCase) == true)
            throw new ChallengeRuntimeException("challenge.flag_template_obsolete", StatusCodes.Status400BadRequest,
                "The {userId} Flag placeholder is no longer supported. Replace it with [GUID].");
        return GZCTF.Features.Imports.Application.ImportedFlagGenerator.Generate(whale ? "ctfd-whale" : "native",
            template, settings.ImportSourceInstance ?? challenge.SourceType, challenge.SourceId, userId);
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
        string? FlagTemplate, string? FlagMode = null, string? ImportSourceInstance = null);
}
