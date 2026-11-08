using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.ChallengeRuntime.Application;
using GZCTF.Features.ChallengeRuntime.Domain;
using GZCTF.Features.ChallengeRuntime.Infrastructure;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using LegacyContainer = GZCTF.Models.Data.Container;

namespace GZCTF.Integration.Test.Tests.Runtime;

[Collection(nameof(IntegrationTestCollection))]
public sealed class EditorTestSessionTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Session_lease_requires_admin_owner_exact_instance_and_session_and_never_credits_progress()
    {
        var adapter = new RecordingRuntime();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILegacyContainerRuntimeAdapter>();
            services.AddSingleton<ILegacyContainerRuntimeAdapter>(adapter);
        }));
        var (owner, client) = await LoginAsync(host, Role.Admin);
        using var ownerClient = client;
        var (_, other) = await LoginAsync(host, Role.Admin);
        using var otherClient = other;
        var (_, student) = await LoginAsync(host, Role.User);
        using var studentClient = student;
        var id = await SeedAsync();
        var path = $"/api/challenges/{id}/instances";
        var session = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsync($"{path}?testSessionId={session}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"{path}?testSessionId={Guid.Empty}", null)).StatusCode);
        var started = await client.PostAsync($"{path}?testSessionId={session}", null);
        started.EnsureSuccessStatusCode();
        var body = await started.Content.ReadFromJsonAsync<JsonElement>();
        var instanceId = body.GetProperty("id").GetGuid();
        var request = new { instanceId, testSessionId = session };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var instance = await db.UserChallengeInstances.SingleAsync(i => i.Id == instanceId);
            Assert.InRange((instance.ExpiresAtUtc!.Value - DateTimeOffset.UtcNow).TotalSeconds, 1, 90);
            Assert.Equal(instance.ExpiresAtUtc, (await db.Containers.FindAsync(instance.ContainerId))!.ExpectStopAt);
            Assert.StartsWith("nynuctf{", instance.AssignedFlag);
            Assert.DoesNotContain("[GUID]", instance.AssignedFlag!);
            Assert.False(adapter.LastRequest!.UseWhaleFlagEnvironment);
            instance.ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(30);
            (await db.Containers.FindAsync(instance.ContainerId))!.ExpectStopAt = instance.ExpiresAtUtc.Value;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync($"{path}/test-heartbeat", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"{path}/test-heartbeat", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"{path}/test-heartbeat", new { instanceId, testSessionId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"{path}/test-heartbeat", request)).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var renewed = await db.UserChallengeInstances.SingleAsync(i => i.Id == instanceId);
            Assert.InRange((renewed.ExpiresAtUtc!.Value - DateTimeOffset.UtcNow).TotalSeconds, 80, 90);
            Assert.Equal(renewed.ExpiresAtUtc, (await db.Containers.FindAsync(renewed.ContainerId))!.ExpectStopAt);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"{path}/test-stop", new { instanceId, testSessionId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(0, adapter.StopCount);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"{path}?testSessionId={Guid.NewGuid()}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await other.PostAsJsonAsync($"{path}/test-stop", request)).StatusCode);
        Assert.Equal(0, adapter.StopCount);
        var submission = await client.PostAsJsonAsync($"/api/challenges/{id}/submissions", new { flag = adapter.LastRequest!.Flag });
        // A test instance can check a flag but never earns learner credit.
        submission.EnsureSuccessStatusCode();
        var rejectedTest = await submission.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(rejectedTest.GetProperty("accepted").GetBoolean());
        Assert.Equal("challenge.test_instance", rejectedTest.GetProperty("rejectionCode").GetString());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.ChallengeProgress.AnyAsync(p => p.UserId == owner && p.ChallengeId == id));
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"{path}/test-stop", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"{path}/test-stop", request)).StatusCode);
        var replacementResponse = await client.PostAsync($"{path}?testSessionId={Guid.NewGuid()}", null);
        replacementResponse.EnsureSuccessStatusCode();
        var replacement = (await replacementResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var stops = adapter.StopCount;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"{path}/test-stop", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"{path}/test-heartbeat", request)).StatusCode);
        Assert.Equal(stops, adapter.StopCount);
        Assert.NotEqual(instanceId, replacement);
        (await client.DeleteAsync(path)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Expired_session_cannot_revive_and_new_editor_cannot_adopt_a_student_or_legacy_test()
    {
        var adapter = new RecordingRuntime();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<ILegacyContainerRuntimeAdapter>(); services.AddSingleton<ILegacyContainerRuntimeAdapter>(adapter); }));
        var (_, client) = await LoginAsync(host, Role.Admin);
        using var ownerClient = client;
        var id = await SeedAsync();
        var path = $"/api/challenges/{id}/instances";
        var session = Guid.NewGuid();
        var first = await client.PostAsync($"{path}?testSessionId={session}", null);
        first.EnsureSuccessStatusCode();
        var instanceId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var instance = await db.UserChallengeInstances.SingleAsync(i => i.Id == instanceId);
            instance.ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
            (await db.Containers.FindAsync(instance.ContainerId))!.ExpectStopAt = instance.ExpiresAtUtc.Value;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"{path}/test-heartbeat", new { instanceId, testSessionId = session })).StatusCode);
        (await client.PostAsync($"{path}?testSessionId={Guid.NewGuid()}", null)).EnsureSuccessStatusCode();
        (await client.DeleteAsync(path)).EnsureSuccessStatusCode();
        // Legacy administrator tests remain explicitly stoppable but cannot be adopted by a page.
        (await client.PostAsync(path, null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"{path}?testSessionId={session}", null)).StatusCode);
        (await client.DeleteAsync(path)).EnsureSuccessStatusCode();
        var (_, learner) = await LoginAsync(host, Role.User);
        using var learnerClient = learner;
        var learnerStart = await learner.PostAsync(path, null);
        learnerStart.EnsureSuccessStatusCode();
        var learnerId = (await learnerStart.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        // Role changes do not allow editor sessions to take over a still-running student instance.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var instance = await db.UserChallengeInstances.SingleAsync(i => i.Id == learnerId);
            var runtime = new ChallengeRuntimeService(db, adapter, scope.ServiceProvider.GetRequiredService<LearningInstanceSettingsService>());
            var error = await Assert.ThrowsAsync<ChallengeRuntimeException>(() => runtime.StartAsync(instance.UserId, id,
                allowDraft: true, testSessionId: session));
            Assert.Equal("challenge.test_session_conflict", error.Code);
            var studentStops = adapter.StopCount;
            await runtime.StopTestAsync(instance.UserId, id, learnerId, session);
            Assert.Equal(studentStops, adapter.StopCount);
        }
        // Guarded stop cannot stop the student's exact instance, even with its known ID.
        var stops = adapter.StopCount;
        (await client.PostAsJsonAsync($"{path}/test-stop", new { instanceId = learnerId, testSessionId = session })).EnsureSuccessStatusCode();
        Assert.Equal(stops, adapter.StopCount);
        (await learner.DeleteAsync(path)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Disconnected_start_still_persists_a_short_lease_before_releasing_the_start_lock()
    {
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Lease!2026", role: Role.Admin);
        var id = await SeedAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adapter = new RecordingRuntime { ReleaseStart = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var runtime = new ChallengeRuntimeService(db, adapter, scope.ServiceProvider.GetRequiredService<LearningInstanceSettingsService>());
        using var disconnected = new CancellationTokenSource();
        var session = Guid.NewGuid();
        var starting = runtime.StartAsync(user.Id, id, disconnected.Token, allowDraft: true, testSessionId: session);
        await adapter.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        disconnected.Cancel();
        adapter.ReleaseStart.SetResult();
        var instance = await starting;
        Assert.InRange((instance.ExpiresAtUtc!.Value - DateTimeOffset.UtcNow).TotalSeconds, 80, 90);
        Assert.Equal(instance.ExpiresAtUtc, (await db.Containers.FindAsync(instance.ContainerId))!.ExpectStopAt);
        Assert.Contains(session.ToString(), instance.RuntimeMetadataJson);
        await runtime.StopTestAsync(user.Id, id, instance.Id, session);
        Assert.Equal(1, adapter.StopCount);
    }

    [Fact]
    public async Task Racing_editor_starts_bind_one_session_before_a_second_editor_can_adopt_it()
    {
        var adapter = new RecordingRuntime { ReleaseStart = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<ILegacyContainerRuntimeAdapter>(); services.AddSingleton<ILegacyContainerRuntimeAdapter>(adapter); }));
        var (_, client) = await LoginAsync(host, Role.Admin);
        using var owner = client;
        var id = await SeedAsync();
        var path = $"/api/challenges/{id}/instances";
        var session = Guid.NewGuid();
        var firstStart = client.PostAsync($"{path}?testSessionId={session}", null);
        await adapter.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var secondStart = client.PostAsync($"{path}?testSessionId={Guid.NewGuid()}", null);
        adapter.ReleaseStart.SetResult();
        var first = await firstStart;
        first.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await secondStart).StatusCode);
        Assert.Equal(1, adapter.StartCount);
        var instanceId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync($"{path}/test-stop", new { instanceId, testSessionId = session })).EnsureSuccessStatusCode();
        Assert.Equal(1, adapter.StopCount);
    }

    [Theory]
    [InlineData("native", null, false)]
    [InlineData("GZCTF-GAME", "gzctf", false)]
    [InlineData("CTFd", null, true)]
    public async Task Static_container_flags_remain_literal_while_Whale_keeps_its_random_environment(string source, string? mode, bool whale)
    {
        var (user, client) = await LoginAsync(factory, Role.Admin);
        using var owner = client;
        var id = await SeedAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var challenge = await db.Challenges.Include(c => c.Flags).SingleAsync(c => c.Id == id);
        challenge.Type = ChallengeType.StaticContainer;
        challenge.SourceType = source;
        challenge.Flags.Clear();
        challenge.Flags.Add(new ChallengeFlag { Kind = ChallengeFlagKind.Static, Value = "literal{{userId}}[GUID]", MetadataJson = "{\"IgnoreCase\":true}" });
        challenge.RuntimeConfigurationJson = JsonSerializer.Serialize(new { Image = "fixture.invalid/test:1", ExposedPort = 80,
            Cpu = 1, MemoryMb = 64, StorageMb = 0, NetworkMode = "Open", FlagMode = mode, FlagTemplate = "obsolete{{userId}}" });
        await db.SaveChangesAsync();
        var adapter = new RecordingRuntime();
        var runtime = new ChallengeRuntimeService(db, adapter, scope.ServiceProvider.GetRequiredService<LearningInstanceSettingsService>());
        await runtime.StartAsync(user, id, allowDraft: true);
        Assert.Equal(whale, adapter.LastRequest!.UseWhaleFlagEnvironment);
        if (whale) Assert.StartsWith("flag{", adapter.LastRequest.Flag);
        else Assert.Equal("literal{{userId}}[GUID]", adapter.LastRequest.Flag);
        await runtime.StopAsync(user, id);
    }

    [Theory]
    [InlineData("native", null, true)]
    [InlineData("GZCTF-GAME", "gzctf", true)]
    [InlineData("CTFd", null, true)]
    [InlineData("native", null, false)]
    [InlineData("GZCTF-GAME", "gzctf", false)]
    [InlineData("CTFd", null, false)]
    public async Task Dynamic_flags_use_custom_templates_and_obsolete_placeholders_reject_before_container_start(string source, string? mode, bool runtimeTemplate)
    {
        var (user, client) = await LoginAsync(factory, Role.Admin);
        using var owner = client;
        var id = await SeedAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var challenge = await db.Challenges.Include(c => c.Flags).SingleAsync(c => c.Id == id);
        challenge.SourceType = source;
        challenge.RuntimeConfigurationJson = JsonSerializer.Serialize(new { Image = "fixture.invalid/test:1", ExposedPort = 80,
            Cpu = 1, MemoryMb = 64, StorageMb = 0, NetworkMode = "Open", FlagMode = mode,
            ImportSourceInstance = "original-server", FlagTemplate = runtimeTemplate ? "nynuctf{[GUID]}" : "" });
        await db.SaveChangesAsync();
        var adapter = new RecordingRuntime();
        var runtime = new ChallengeRuntimeService(db, adapter, scope.ServiceProvider.GetRequiredService<LearningInstanceSettingsService>());
        var instance = await runtime.StartAsync(user, id, allowDraft: true);
        Assert.StartsWith("nynuctf{", instance.AssignedFlag);
        Assert.True(Guid.TryParse(instance.AssignedFlag![8..^1], out _));
        Assert.Equal(source == "CTFd", adapter.LastRequest!.UseWhaleFlagEnvironment);
        await runtime.StopAsync(user, id);
        if (runtimeTemplate) challenge.RuntimeConfigurationJson = challenge.RuntimeConfigurationJson.Replace("nynuctf{[GUID]}", "flag{{userId}}");
        else challenge.Flags.Single().Template = "flag{{userId}}";
        await db.SaveChangesAsync();
        var previousRequest = adapter.LastRequest;
        var error = await Assert.ThrowsAsync<ChallengeRuntimeException>(() => runtime.StartAsync(user, id, allowDraft: true));
        Assert.Equal("challenge.flag_template_obsolete", error.Code);
        Assert.Same(previousRequest, adapter.LastRequest);
    }

    private async Task<Guid> SeedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var challenge = new Challenge { Type = ChallengeType.DynamicContainer,
            PublicationState = ChallengePublicationState.Published, IsEnabled = true,
            SourceType = "native", SourceId = Guid.NewGuid().ToString("N"),
            Localizations = [new ChallengeLocalization { Locale = "en", Title = "Editor test" }],
            Flags = [new ChallengeFlag { Kind = ChallengeFlagKind.Template, Template = "nynuctf{[GUID]}" }],
            RuntimeConfigurationJson = JsonSerializer.Serialize(new { Image = "fixture.invalid/editor:1", ExposedPort = 80,
                Cpu = 1, MemoryMb = 64, StorageMb = 0, NetworkMode = "Open", FlagTemplate = "nynuctf{[GUID]}" }) };
        db.Challenges.Add(challenge);
        await db.SaveRuntimeFixtureAsync();
        return challenge.Id;
    }

    private async Task<(Guid, HttpClient)> LoginAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host, Role role)
    {
        const string password = "Editor!Test2026";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password, role: role);
        var client = host.CreateClient();
        (await client.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = user.UserName, Password = password })).EnsureSuccessStatusCode();
        return (user.Id, client);
    }

    private sealed class RecordingRuntime : ILegacyContainerRuntimeAdapter
    {
        public int StopCount { get; private set; }
        public int StartCount { get; private set; }
        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? ReleaseStart { get; init; }
        public CanonicalContainerRequest? LastRequest { get; private set; }
        public async Task<LegacyContainer?> StartAsync(CanonicalContainerRequest request, CancellationToken token)
        {
            StartCount++;
            LastRequest = request;
            StartEntered.TrySetResult();
            if (ReleaseStart is not null) await ReleaseStart.Task.WaitAsync(token);
            return new LegacyContainer { Image = request.Image,
                ContainerId = Guid.NewGuid().ToString(), Status = ContainerStatus.Running, IP = "127.0.0.1", Port = 80 };
        }
        public Task StopAsync(LegacyContainer container, CancellationToken token)
        { StopCount++; container.Status = ContainerStatus.Destroyed; return Task.CompletedTask; }
    }
}
