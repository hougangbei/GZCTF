using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Features.Auditing.Domain;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Auditing;

[Collection(nameof(IntegrationTestCollection))]
public sealed class AuditApiTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Audit_api_is_admin_only_and_has_stable_server_pagination()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var ids = Enumerable.Range(0, 25).Select(_ => Guid.CreateVersion7()).ToArray();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditEvents.AddRange(ids.Select((id, index) => new AuditEvent
            {
                Id = id,
                OccurredAtUtc = timestamp,
                ActorName = $"operator-{index}",
                ActorKind = "admin",
                Category = "audit-test",
                Action = "users.update",
                TargetType = "user",
                TargetId = Guid.CreateVersion7().ToString(),
                TargetName = "test target",
                Succeeded = index % 2 == 0,
                HttpStatus = 200,
                RequestId = Guid.NewGuid().ToString("N")
            }));
            await db.SaveChangesAsync();
        }

        using var admin = await CreateClientAsync(Role.Admin);
        using var user = await CreateClientAsync(Role.User);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.CreateClient().GetAsync("/api/admin/audit-events")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/audit-events")).StatusCode);

        var first = await admin.GetFromJsonAsync<JsonDocument>("/api/admin/audit-events?category=audit-test");
        Assert.Equal(20, first!.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(25, first.RootElement.GetProperty("total").GetInt32());
        var firstIds = first.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid()).ToArray();
        Guid[] expectedIds;
        using (var scope = factory.Services.CreateScope())
        {
            expectedIds = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditEvents.AsNoTracking()
                .Where(item => item.Category == "audit-test").OrderByDescending(item => item.OccurredAtUtc)
                .ThenByDescending(item => item.Id).Take(20).Select(item => item.Id).ToArrayAsync();
        }
        Assert.Equal(expectedIds, firstIds);

        var second = await admin.GetFromJsonAsync<JsonDocument>("/api/admin/audit-events?category=audit-test&page=2&pageSize=20");
        Assert.Equal(5, second!.RootElement.GetProperty("items").GetArrayLength());
        Guid[] expectedSecond;
        using (var scope = factory.Services.CreateScope())
        {
            expectedSecond = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditEvents.AsNoTracking()
                .Where(item => item.Category == "audit-test").OrderByDescending(item => item.OccurredAtUtc)
                .ThenByDescending(item => item.Id).Skip(20).Take(20).Select(item => item.Id).ToArrayAsync();
        }
        Assert.Equal(expectedSecond, second.RootElement.GetProperty("items")
            .EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/audit-events?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/admin/audit-events?page=2147483647&pageSize=100")).StatusCode);
        var searched = await admin.GetFromJsonAsync<JsonDocument>(
            "/api/admin/audit-events?category=audit-test&search=test%20target&succeeded=true");
        Assert.Equal(13, searched!.RootElement.GetProperty("total").GetInt32());

        const string downgradedPassword = "Audit!DowngradedAdmin1";
        var downgradedUser = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), downgradedPassword, role: Role.Admin);
        using var downgraded = factory.CreateClient();
        var login = await downgraded.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = downgradedUser.UserName, Password = downgradedPassword });
        login.EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var persisted = await db.Users.SingleAsync(item => item.Id == downgradedUser.Id);
            persisted.Role = Role.User;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await downgraded.GetAsync("/api/admin/audit-events")).StatusCode);
    }

    [Fact]
    public async Task System_log_api_keeps_source_and_exception_and_returns_total_page_data()
    {
        var timestamp = DateTimeOffset.UtcNow;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Logs.AddRange(
                new LogModel { TimeUtc = timestamp, Level = "AuditTest", Logger = "Audit.Source", Message = "entry one", Exception = "stack one" },
                new LogModel { TimeUtc = timestamp, Level = "AuditTest", Logger = "Audit.Source", Message = "entry two", Exception = "stack two" });
            await db.SaveChangesAsync();
        }

        using var admin = await CreateClientAsync(Role.Admin);
        var page = await admin.GetFromJsonAsync<JsonDocument>("/api/Admin/Logs?level=AuditTest&page=1&pageSize=1");
        Assert.Equal(2, page!.RootElement.GetProperty("total").GetInt32());
        Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
        var item = page.RootElement.GetProperty("items")[0];
        Assert.Equal("Audit.Source", item.GetProperty("source").GetString());
        Assert.StartsWith("stack ", item.GetProperty("exception").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/Admin/Logs?page=2147483647&pageSize=100")).StatusCode);
    }

    [Fact]
    public async Task Flag_original_is_only_returned_by_unexpired_detail_for_an_admin()
    {
        const string submitted = "flag{private-submission}";
        var activeId = Guid.CreateVersion7();
        var expiredId = Guid.CreateVersion7();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var correctProtector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("GZCTF.Auditing.FlagAttempt.v1");
            var value = correctProtector.Protect(submitted);
            db.FlagAttemptLogs.AddRange(
                new FlagAttemptLog
                {
                    Id = activeId, OccurredAtUtc = DateTimeOffset.UtcNow.AddDays(-2), UserId = Guid.CreateVersion7(),
                    UserName = "learner", ChallengeId = Guid.CreateVersion7(), ChallengeName = "Challenge",
                    Outcome = "incorrect", ProtectedSubmittedFlag = value
                },
                new FlagAttemptLog
                {
                    Id = expiredId, OccurredAtUtc = DateTimeOffset.UtcNow.AddDays(-30), UserId = Guid.CreateVersion7(),
                    UserName = "learner", ChallengeId = Guid.CreateVersion7(), Outcome = "incorrect",
                    ProtectedSubmittedFlag = value
                });
            await db.SaveChangesAsync();
        }

        using var admin = await CreateClientAsync(Role.Admin);
        using var ordinary = await CreateClientAsync(Role.User);
        var list = await admin.GetStringAsync("/api/admin/flag-attempts");
        Assert.DoesNotContain(submitted, list, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await ordinary.GetAsync($"/api/admin/flag-attempts/{activeId}")).StatusCode);
        var detail = await admin.GetFromJsonAsync<JsonDocument>($"/api/admin/flag-attempts/{activeId}");
        Assert.Equal(submitted, detail!.RootElement.GetProperty("submittedFlag").GetString());
        Assert.True(detail.RootElement.GetProperty("originalAvailable").GetBoolean());
        var expired = await admin.GetFromJsonAsync<JsonDocument>($"/api/admin/flag-attempts/{expiredId}");
        Assert.Null(expired!.RootElement.GetProperty("submittedFlag").GetString());
        Assert.False(expired.RootElement.GetProperty("originalAvailable").GetBoolean());
    }

    [Fact]
    public async Task Admin_action_writes_one_safe_success_and_failure_event()
    {
        const string password = "Audit!ActionTestAdmin1";
        var actor = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password,
            role: Role.Admin);
        using var admin = factory.CreateClient();
        var login = await admin.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = actor.UserName, Password = password });
        login.EnsureSuccessStatusCode();
        var created = await admin.PostAsJsonAsync("/api/admin/dashboards", new
        {
            name = $"Audit-{Guid.NewGuid():N}", topCount = 10, privateValue = "never-audit-this"
        });
        created.EnsureSuccessStatusCode();
        var invalid = await admin.PostAsJsonAsync("/api/admin/dashboards", new { name = " ", topCount = 10 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.AuditEvents.Where(item => item.ActorId == actor.Id && item.Action == "dashboards.create")
            .OrderBy(item => item.OccurredAtUtc)
            .ToArrayAsync();
        Assert.Equal(2, rows.Length);
        Assert.True(rows[0].Succeeded);
        Assert.Equal("admin", rows[0].ActorKind);
        Assert.StartsWith("Audit-", rows[0].TargetName);
        Assert.False(rows[1].Succeeded);
        Assert.Equal("validation_failed", rows[1].ErrorCode);
        Assert.DoesNotContain("never-audit-this", System.Text.Json.JsonSerializer.Serialize(rows));
    }

    [Fact]
    public async Task Authenticated_denial_of_admin_mutation_is_audited_without_input_values()
    {
        const string privateInput = "do-not-store-this-input";
        const string password = "Audit!DeniedMutation1";
        var actor = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password,
            role: Role.User);
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = actor.UserName, Password = password });
        login.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/admin/dashboards", new
        {
            name = privateInput, topCount = 10
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditEvents.SingleAsync(item => item.ActorId == actor.Id &&
            item.Action == "dashboards.create");
        Assert.False(audit.Succeeded);
        Assert.Equal("forbidden", audit.ErrorCode);
        Assert.DoesNotContain(privateInput, System.Text.Json.JsonSerializer.Serialize(audit));
    }

    [Fact]
    public async Task Password_reset_response_is_not_copied_to_the_audit_event()
    {
        var target = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Before!Reset1");
        using var admin = await CreateClientAsync(Role.Admin);
        var response = await admin.DeleteAsync($"/api/Admin/Users/{target.Id}/Password");
        response.EnsureSuccessStatusCode();
        var generatedPassword = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(generatedPassword));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditEvents.SingleAsync(item => item.Action == "users.password.reset" &&
            item.TargetId == target.Id.ToString());
        Assert.True(audit.Succeeded);
        Assert.DoesNotContain(generatedPassword, System.Text.Json.JsonSerializer.Serialize(audit), StringComparison.Ordinal);
    }

    private async Task<HttpClient> CreateClientAsync(Role role)
    {
        const string password = "Audit!TestUser1";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password, role: role);
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = user.UserName, Password = password });
        response.EnsureSuccessStatusCode();
        return client;
    }
}
