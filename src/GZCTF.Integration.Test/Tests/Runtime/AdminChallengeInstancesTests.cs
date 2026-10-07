using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.ChallengeRuntime.Domain;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using CanonicalChallenge = GZCTF.Features.ChallengeLibrary.Domain.Challenge;

namespace GZCTF.Integration.Test.Tests.Runtime;

[Collection(nameof(IntegrationTestCollection))]
public sealed class AdminChallengeInstancesTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Admin_can_list_and_stop_a_learning_challenge_instance()
    {
        var learner = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(),
            "Learner!2026");
        var instanceId = Guid.CreateVersion7();
        var containerId = Guid.CreateVersion7();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var challenge = new CanonicalChallenge
            {
                Id = Guid.CreateVersion7(), Type = ChallengeType.StaticContainer,
                PublicationState = ChallengePublicationState.Published, IsEnabled = true,
                SourceType = "native", SourceId = Guid.NewGuid().ToString("N"),
                Localizations = [new ChallengeLocalization { Locale = "en", Title = "Instance challenge" }]
            };
            db.Challenges.Add(challenge);
            db.Containers.Add(new GZCTF.Models.Data.Container
            {
                Id = containerId, Image = "registry.test/box:1", ContainerId = "box",
                Status = ContainerStatus.Running,
                IP = "127.0.0.1", Port = 8080,
                ExpectStopAt = DateTimeOffset.UtcNow.AddHours(2)
            });
            db.UserChallengeInstances.Add(new UserChallengeInstance
            {
                Id = instanceId, UserId = learner.Id, ChallengeId = challenge.Id,
                Status = ChallengeInstanceStatus.Running, IsActive = true,
                StartedAtUtc = DateTimeOffset.UtcNow,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2), ContainerId = containerId
            });
            await db.SaveChangesAsync();
        }

        using var visitor = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await visitor.GetAsync("/api/admin/challenge-instances")).StatusCode);

        using var admin = await CreateAdminClientAsync();
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/admin/challenge-instances");
        var row = Assert.Single(list.EnumerateArray(), item => item.GetProperty("id").GetGuid() == instanceId);
        Assert.Equal(learner.UserName, row.GetProperty("userName").GetString());
        Assert.Equal("Instance challenge", row.GetProperty("challengeTitle").GetString());

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/admin/challenge-instances/{instanceId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/admin/challenge-instances/{instanceId}")).StatusCode);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stopped = await verifyDb.UserChallengeInstances.SingleAsync(item => item.Id == instanceId);
        Assert.False(stopped.IsActive);
        Assert.Equal(ChallengeInstanceStatus.Stopped, stopped.Status);
    }

    [Fact]
    public async Task Admin_can_set_learning_container_count_and_lifetime()
    {
        using var visitor = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await visitor.GetAsync("/api/admin/challenge-instances/settings")).StatusCode);

        using var admin = await CreateAdminClientAsync();
        var original = await admin.GetFromJsonAsync<JsonElement>("/api/admin/challenge-instances/settings");
        Assert.Equal(2, original.GetProperty("maxConcurrentPerUser").GetInt32());
        Assert.Equal(120, original.GetProperty("lifetimeMinutes").GetInt32());

        try
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await admin.PutAsJsonAsync("/api/admin/challenge-instances/settings",
                    new { maxConcurrentPerUser = 0, lifetimeMinutes = 90 })).StatusCode);
            (await admin.PutAsJsonAsync("/api/admin/challenge-instances/settings",
                new { maxConcurrentPerUser = 3, lifetimeMinutes = 90 })).EnsureSuccessStatusCode();
            var saved = await admin.GetFromJsonAsync<JsonElement>("/api/admin/challenge-instances/settings");
            Assert.Equal(3, saved.GetProperty("maxConcurrentPerUser").GetInt32());
            Assert.Equal(90, saved.GetProperty("lifetimeMinutes").GetInt32());
        }
        finally
        {
            (await admin.PutAsJsonAsync("/api/admin/challenge-instances/settings",
                new { maxConcurrentPerUser = 2, lifetimeMinutes = 120 })).EnsureSuccessStatusCode();
        }
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        const string password = "Instance!Admin2026";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password,
            role: Role.Admin);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = user.UserName, Password = password });
        login.EnsureSuccessStatusCode();
        return client;
    }
}
