using System.Net.Http;
using System.Net.Sockets;
using GZCTF.Features.ChallengeRuntime.Application;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Services.Container.Manager;
using GZCTF.Utils;

namespace GZCTF.Features.ChallengeRuntime.Infrastructure;

public sealed record CanonicalContainerRequest(
    Guid UserId,
    Guid ChallengeId,
    string Image,
    int ExposedPort,
    int Cpu,
    int MemoryMb,
    int StorageMb,
    string NetworkMode,
    string? Flag);

public interface ILegacyContainerRuntimeAdapter
{
    Task<Container?> StartAsync(CanonicalContainerRequest request, CancellationToken token);
    Task StopAsync(Container container, CancellationToken token);
}

public sealed class LegacyContainerRuntimeAdapter(IServiceProvider services) : ILegacyContainerRuntimeAdapter
{
    public async Task<Container?> StartAsync(CanonicalContainerRequest request, CancellationToken token)
    {
        var networkMode = Enum.TryParse<NetworkMode>(request.NetworkMode, true, out var parsed)
            ? parsed
            : NetworkMode.Open;
        var manager = ResolveManager();
        try
        {
            var container = await manager.CreateContainerAsync(new ContainerConfig
            {
                Image = request.Image,
                TeamId = request.UserId.ToString("N"),
                ChallengeId = StableChallengeId(request.ChallengeId),
                UserId = request.UserId,
                ExposedPort = request.ExposedPort,
                Flag = request.Flag,
                CPUCount = request.Cpu,
                MemoryLimit = request.MemoryMb,
                StorageLimit = request.StorageMb,
                NetworkMode = networkMode
            }, token);
            return container ?? throw ChallengeRuntimeException.StartFailed();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (ChallengeRuntimeException) { throw; }
        catch (Exception error)
        {
            throw IsConnectionFailure(error)
                ? ChallengeRuntimeException.Unavailable(error)
                : ChallengeRuntimeException.StartFailed(error);
        }
    }

    public async Task StopAsync(Container container, CancellationToken token)
    {
        var manager = ResolveManager();
        try
        {
            await manager.DestroyContainerAsync(container, token);
            if (container.Status != ContainerStatus.Destroyed)
                throw ChallengeRuntimeException.StopFailed();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (ChallengeRuntimeException) { throw; }
        catch (Exception error)
        {
            throw IsConnectionFailure(error)
                ? ChallengeRuntimeException.Unavailable(error)
                : ChallengeRuntimeException.StopFailed(error);
        }
    }

    private IContainerManager ResolveManager()
    {
        // Controllers also serve database-only queries: resolve the external host only
        // inside an operation, where configuration/connectivity failures can be handled.
        try { return services.GetRequiredService<IContainerManager>(); }
        catch (Exception error) { throw ChallengeRuntimeException.Unavailable(error); }
    }

    private static bool IsConnectionFailure(Exception error) =>
        error is HttpRequestException or IOException or SocketException ||
        error.InnerException is { } inner && IsConnectionFailure(inner);

    private static int StableChallengeId(Guid challengeId) =>
        BitConverter.ToInt32(challengeId.ToByteArray(), 0) & int.MaxValue;
}
