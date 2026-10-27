using System.Net.NetworkInformation;
using Docker.DotNet;
using GZCTF.Integration.Test.Base;
using GZCTF.Models.Internal;
using GZCTF.Services.Container.Manager;
using GZCTF.Services.Container.Provider;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Runtime;

[Collection(nameof(IntegrationTestCollection))]
public sealed class DockerPortRangeTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Concurrent_instances_stay_in_pool_and_exhaustion_cleans_up_before_reuse()
    {
        _ = factory.Services;
        var prefix = "gzctf-pool-" + Guid.NewGuid().ToString("N");
        using var networkClient = new DockerClientBuilder().Build();
        await using var networks = new IsolatedDockerNetworks(networkClient, prefix);
        await networks.CreateAsync();
        // Avoid both existing Docker publications and host listeners, below the
        // usual ephemeral range used by other test containers.
        var containers = await networkClient.Containers.ListContainersAsync(new());
        var occupied = containers.SelectMany(c => c.Ports ?? []).Where(p => p.PublicPort.HasValue).Select(p => (int)p.PublicPort!.Value).ToHashSet();
        if (OperatingSystem.IsLinux())
            foreach (var endpoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                occupied.Add(endpoint.Port);
        var start = Enumerable.Range(20000, 4999).First(p => !occupied.Contains(p) && !occupied.Contains(p + 1));
        var provider = new DockerProvider(Options.Create(new ContainerProvider
        {
            PublicEntry = "campus-entry.example",
            DockerConfig = new GZCTF.Models.Internal.DockerConfig
            {
                Uri = Environment.GetEnvironmentVariable("DOCKER_HOST") ?? "",
                ChallengeNetwork = prefix,
                HostPortRange = $"{start}-{start + 1}"
            }
        }), Options.Create(new RegistrySet<RegistryConfig>()), NullLogger<DockerProvider>.Instance);
        using var client = provider.GetProvider();
        var manager = new DockerManager(provider, NullLogger<DockerManager>.Instance);
        var created = new List<GZCTF.Models.Data.Container>();
        var configs = Enumerable.Range(1, 4).Select(i => new ContainerConfig
        {
            Image = "nginx:1.27", TeamId = prefix, UserId = Guid.NewGuid(), ChallengeId = i,
            ExposedPort = 80, MemoryLimit = 64, CPUCount = 1, Flag = "flag{port-pool}"
        }).ToArray();
        try
        {
            var instances = await Task.WhenAll(configs.Take(2).Select(c => manager.CreateContainerAsync(c)));
            foreach (var instance in instances)
            {
                Assert.NotNull(instance);
                created.Add(instance);
                Assert.InRange(instance.PublicPort!.Value, start, start + 1);
                Assert.Equal("campus-entry.example", instance.PublicIP);
            }
            Assert.NotEqual(instances[0]!.PublicPort, instances[1]!.PublicPort);
            var failedName = DockerMetadata.GetName(configs[2]);
            await Assert.ThrowsAnyAsync<Exception>(() => manager.CreateContainerAsync(configs[2]));
            await Assert.ThrowsAsync<DockerContainerNotFoundException>(() => client.Containers.InspectContainerAsync(failedName));
            var releasedPort = instances[0]!.PublicPort;
            await manager.DestroyContainerAsync(instances[0]!);
            var replacement = await manager.CreateContainerAsync(configs[3]);
            Assert.NotNull(replacement);
            created.Add(replacement);
            Assert.Equal(releasedPort, replacement.PublicPort);
        }
        finally
        {
            foreach (var instance in created) await manager.DestroyContainerAsync(instance);
            foreach (var config in configs)
            {
                try { await client.Containers.RemoveContainerAsync(DockerMetadata.GetName(config), new() { Force = true }); }
                catch (DockerContainerNotFoundException) { }
            }
        }
    }
}
