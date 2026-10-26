using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Features.ChallengeRuntime.Infrastructure;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Services.Container.Manager;
using GZCTF.Utils;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Test.UnitTests.Features.ChallengeRuntime;

public sealed class RuntimeDependencyTests
{
    private static CanonicalContainerRequest Request => new(Guid.NewGuid(), Guid.NewGuid(),
        "test/image:latest", 8080, 1, 128, 256, "Open", "flag{test}");

    [Fact]
    public async Task Adapter_activation_defers_unavailable_runtime_until_an_operation_and_hides_endpoint()
    {
        var services = new ServiceCollection();
        var resolutions = 0;
        services.AddSingleton<IContainerManager>(_ =>
        {
            resolutions++;
            throw new InvalidOperationException("https://user:secret@private-host/docker is unavailable");
        });
        using var provider = services.BuildServiceProvider();
        var adapter = ActivatorUtilities.CreateInstance<LegacyContainerRuntimeAdapter>(provider);
        Assert.Equal(0, resolutions);
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            adapter.StartAsync(Request, CancellationToken.None));
        Assert.Equal("challenge.runtime_unavailable", error.Message);
        Assert.Equal(1, resolutions);
    }

    [Fact]
    public async Task Failed_start_returns_stable_failure_instead_of_null()
    {
        using var provider = Provider(new TestManager());
        var adapter = ActivatorUtilities.CreateInstance<LegacyContainerRuntimeAdapter>(provider);
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            adapter.StartAsync(Request, CancellationToken.None));
        Assert.Equal("challenge.container_start_failed", error.Message);
    }

    [Fact]
    public async Task Failed_stop_does_not_report_success()
    {
        using var provider = Provider(new TestManager());
        var adapter = ActivatorUtilities.CreateInstance<LegacyContainerRuntimeAdapter>(provider);
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            adapter.StopAsync(new Container { Status = ContainerStatus.Running }, CancellationToken.None));
        Assert.Equal("challenge.container_stop_failed", error.Message);
    }

    private static ServiceProvider Provider(IContainerManager manager) =>
        new ServiceCollection().AddSingleton(manager).BuildServiceProvider();

    private sealed class TestManager : IContainerManager
    {
        public Task<Container?> CreateContainerAsync(ContainerConfig config, CancellationToken token = default) =>
            Task.FromResult<Container?>(null);
        public Task DestroyContainerAsync(Container container, CancellationToken token = default) => Task.CompletedTask;
    }
}
