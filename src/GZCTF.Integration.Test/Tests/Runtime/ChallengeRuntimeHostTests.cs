using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Docker.DotNet;
using GZCTF.Utils;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.ChallengeRuntime.Application;
using GZCTF.Features.ChallengeRuntime.Infrastructure;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Internal;
using GZCTF.Models.Request.Account;
using GZCTF.Services.Container.Manager;
using GZCTF.Services.Container.Provider;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using ContainerStatus = GZCTF.Utils.ContainerStatus;

namespace GZCTF.Integration.Test.Tests.Runtime;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ChallengeRuntimeHostTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Unavailable_host_keeps_reads_available_and_returns_safe_actionable_start_error()
    {
        const string password = "Runtime!2026Test";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password,
            role: GZCTF.Utils.Role.Admin);
        var id = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Challenges.Add(new Challenge { Id = id, Type = ChallengeType.StaticContainer,
                PublicationState = ChallengePublicationState.Published, IsEnabled = true,
                SourceType = "runtime-host-test", SourceId = id.ToString(),
                Localizations = [new ChallengeLocalization { Locale = "en", Title = "Runtime test" }],
                Flags = [new ChallengeFlag { Kind = ChallengeFlagKind.Static, Value = "flag{runtime}" }],
                RuntimeConfigurationJson = "{\"Image\":\"nginx:1.27\",\"ExposedPort\":80,\"Cpu\":1,\"MemoryMb\":64,\"StorageMb\":256,\"NetworkMode\":\"Open\"}" });
            await db.SaveChangesAsync();
        }
        var resolutions = 0;
        using var unavailable = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IContainerManager>();
            services.AddSingleton<IContainerManager>(_ => { resolutions++;
                throw new IOException("unix:///private/secret-host.sock password=secret"); });
        }));
        using var client = unavailable.CreateClient();
        (await client.PostAsJsonAsync("/api/Account/LogIn", new LoginModel
            { UserName = user.UserName, Password = password })).EnsureSuccessStatusCode();
        (await client.GetAsync("/api/admin/challenge-instances/settings")).EnsureSuccessStatusCode();
        (await client.GetAsync("/api/admin/challenge-instances")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/challenges/{id}/instances")).StatusCode);
        Assert.Equal(0, resolutions);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var container = new GZCTF.Models.Data.Container { ContainerId = "unreachable-expired-test",
                Image = "nginx:1.27", Status = ContainerStatus.Running,
                ExpectStopAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
            db.Containers.Add(container);
            db.UserChallengeInstances.Add(new GZCTF.Features.ChallengeRuntime.Domain.UserChallengeInstance
            { UserId = user.Id, ChallengeId = id, ContainerId = container.Id,
                Status = GZCTF.Features.ChallengeRuntime.Domain.ChallengeInstanceStatus.Running,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/challenges/{id}/instances")).StatusCode);
        Assert.Equal(0, resolutions);
        var response = await client.PostAsync($"/api/challenges/{id}/instances", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal("challenge.runtime_unavailable", document.RootElement.GetProperty("code").GetString());
        Assert.Contains("challenge host connection", document.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("secret", body);
        Assert.DoesNotContain("private", body);
        Assert.Equal(1, resolutions);
    }

    [Fact]
    public async Task Configured_host_supports_the_real_HTTP_instance_lifecycle()
    {
        const string password = "Runtime!2026Success";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password);
        var id = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Challenges.Add(new Challenge { Id = id, Type = ChallengeType.StaticContainer,
                PublicationState = ChallengePublicationState.Published, IsEnabled = true,
                SourceType = "runtime-success-test", SourceId = id.ToString(),
                Localizations = [new ChallengeLocalization { Locale = "en", Title = "HTTP runtime test" }],
                Flags = [new ChallengeFlag { Kind = ChallengeFlagKind.Static, Value = "flag{http-runtime}" }],
                RuntimeConfigurationJson = JsonSerializer.Serialize(new { Image = "nginx:1.27", ExposedPort = 80,
                    Cpu = 10, MemoryMb = 64, StorageMb = 256, NetworkMode = "Open" }) });
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/Account/LogIn", new LoginModel
            { UserName = user.UserName, Password = password })).EnsureSuccessStatusCode();
        var path = $"/api/challenges/{id}/instances";
        try
        {
            var start = await client.PostAsync(path, null);
            start.EnsureSuccessStatusCode();
            var running = (await start.Content.ReadFromJsonAsync<JsonElement>());
            Assert.Equal((int)GZCTF.Features.ChallengeRuntime.Domain.ChallengeInstanceStatus.Running,
                running.GetProperty("status").GetInt32());
            var instanceId = running.GetProperty("id").GetGuid();
            var current = await client.GetFromJsonAsync<JsonElement>(path);
            Assert.Equal(instanceId, current.GetProperty("id").GetGuid());
            var extend = await client.PostAsync($"{path}/extend", null);
            extend.EnsureSuccessStatusCode();
            var extended = await extend.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(extended.GetProperty("expiresAtUtc").GetInt64() >=
                        running.GetProperty("expiresAtUtc").GetInt64());
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var instance = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                db.UserChallengeInstances, item => item.Id == instanceId);
            var container = await db.Containers.FindAsync(instance.ContainerId);
            Assert.NotNull(container);
            Assert.Equal(instance.ExpiresAtUtc, container.ExpectStopAt);
            Assert.Equal("flag{http-runtime}", instance.AssignedFlag);
        }
        finally
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(path)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Local_docker_can_start_access_and_stop_an_isolated_container()
    {
        _ = factory.Services; // Initialize the application's localizer used by the legacy manager.
        var prefix = $"gzctf-runtime-test-{Guid.NewGuid():N}";
        using var networkClient = new DockerClientBuilder().Build();
        await using var networks = new IsolatedDockerNetworks(networkClient, prefix);
        await networks.CreateAsync();
        var provider = new DockerProvider(Options.Create(new ContainerProvider
        {
            DockerConfig = new GZCTF.Models.Internal.DockerConfig { Uri = Environment.GetEnvironmentVariable("DOCKER_HOST") ?? "",
                ChallengeNetwork = prefix }, PublicEntry = "localhost"
        }), Options.Create(new RegistrySet<RegistryConfig>()), NullLogger<DockerProvider>.Instance);
        var client = provider.GetProvider();
        var manager = new DockerManager(provider, NullLogger<DockerManager>.Instance);
        GZCTF.Models.Data.Container? container = null;
        try
        {
            container = await manager.CreateContainerAsync(new ContainerConfig
            {
                Image = "nginx:1.27", TeamId = prefix, UserId = Guid.NewGuid(), ChallengeId = 1,
                Flag = "flag{runtime-test}", ExposedPort = 80, MemoryLimit = 64, CPUCount = 1
            });
            Assert.NotNull(container);
            Assert.Equal(ContainerStatus.Running, container.Status);
            Assert.NotNull(container.PublicPort);
            using var http = new HttpClient();
            var info = await client.Containers.InspectContainerAsync(container.ContainerId);
            Assert.Equal(64 * 1024 * 1024, info.HostConfig!.Memory);
            Assert.Equal(100_000_000, info.HostConfig.NanoCPUs);
            Assert.Contains("GZCTF_FLAG=flag{runtime-test}", info.Config!.Env);
            string? page = null;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                try { page = await http.GetStringAsync($"http://localhost:{container.PublicPort}"); break; }
                catch (HttpRequestException) when (attempt < 39) { await Task.Delay(250); }
            }
            Assert.Contains("Welcome to nginx", page);
            await manager.DestroyContainerAsync(container);
            Assert.Equal(ContainerStatus.Destroyed, container.Status);

            var fixture = await client.Containers.CreateContainerAsync(new Docker.DotNet.Models.CreateContainerParameters
            { Image = "nginx:1.27", Entrypoint = ["/gzctf-test-nonexistent-executable"] });
            string? fixtureImage = null;
            string? failedName = null;
            try
            {
                var committed = await client.Images.CommitContainerChangesAsync(
                    new Docker.DotNet.Models.CommitContainerChangesParameters { ContainerID = fixture.ID });
                fixtureImage = committed.ID;
                var config = new ContainerConfig { Image = fixtureImage, TeamId = prefix,
                    UserId = Guid.NewGuid(), ChallengeId = 2, Flag = "flag{failed-start}", ExposedPort = 80 };
                failedName = DockerMetadata.GetName(config);
                await Assert.ThrowsAnyAsync<Exception>(() => manager.CreateContainerAsync(config));
                await Assert.ThrowsAsync<DockerContainerNotFoundException>(() =>
                    client.Containers.InspectContainerAsync(failedName));
            }
            finally
            {
                if (failedName is not null)
                    try { await client.Containers.RemoveContainerAsync(failedName, new() { Force = true }); }
                    catch (DockerContainerNotFoundException) { }
                await client.Containers.RemoveContainerAsync(fixture.ID, new() { Force = true });
                if (fixtureImage is not null)
                    await client.Images.DeleteImageAsync(fixtureImage, new() { Force = true });
            }
        }
        finally
        {
            if (container is not null) await manager.DestroyContainerAsync(container);
            client.Dispose();
        }
    }
}
