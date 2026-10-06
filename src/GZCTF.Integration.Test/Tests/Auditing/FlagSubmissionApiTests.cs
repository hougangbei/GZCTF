using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Features.Auditing.Domain;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Auditing;

[Collection(nameof(IntegrationTestCollection))]
public sealed class FlagSubmissionApiTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Every_authenticated_submission_attempt_is_recorded_without_plaintext_in_generic_logs()
    {
        const string expectedFlag = "flag{endpoint-expected-value}";
        const string incorrectFlag = "flag{endpoint-wrong-value}";
        var user = await TestDataSeeder.CreateUserAsync(
            factory.Services, TestDataSeeder.RandomName(), "Flag!SubmissionApi1", role: Role.User);
        var challengeId = Guid.CreateVersion7();
        var inaccessibleChallengeId = Guid.CreateVersion7();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Challenges.Add(new Challenge
            {
                Id = challengeId,
                Type = ChallengeType.StaticAttachment,
                PublicationState = ChallengePublicationState.Published,
                SourceType = "audit-api-test",
                SourceId = challengeId.ToString("N"),
                Localizations = [new ChallengeLocalization { Locale = "en", Title = "Audit Flag Challenge", Summary = "Test", Body = "Test" }],
                Flags = [new ChallengeFlag { Kind = ChallengeFlagKind.Static, Value = expectedFlag }]
            });
            await db.SaveChangesAsync();
        }

        using var client = await LoginAsync(user.UserName, user.Password);
        var incorrect = await client.PostAsJsonAsync($"/api/challenges/{challengeId}/submissions", new { flag = incorrectFlag });
        incorrect.EnsureSuccessStatusCode();
        var incorrectSubmissionId = (await incorrect.Content.ReadFromJsonAsync<JsonDocument>())!
            .RootElement.GetProperty("submissionId").GetGuid();
        var accepted = await client.PostAsJsonAsync($"/api/challenges/{challengeId}/submissions", new { flag = expectedFlag });
        accepted.EnsureSuccessStatusCode();
        var acceptedSubmissionId = (await accepted.Content.ReadFromJsonAsync<JsonDocument>())!
            .RootElement.GetProperty("submissionId").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/challenges/{challengeId}/submissions", new { flag = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"/api/challenges/{inaccessibleChallengeId}/submissions", new { flag = incorrectFlag })).StatusCode);

        using (var anonymous = factory.CreateClient())
        {
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await anonymous.PostAsJsonAsync($"/api/challenges/{challengeId}/submissions", new { flag = incorrectFlag })).StatusCode);
        }

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attempts = await verifyDb.FlagAttemptLogs.Where(item => item.UserId == user.Id)
            .OrderBy(item => item.OccurredAtUtc).ToArrayAsync();
        Assert.Equal(4, attempts.Length);
        Assert.Equal(new[] { "incorrect", "accepted", "rejected", "rejected" }, attempts.Select(item => item.Outcome));
        Assert.Contains(attempts, item => item.SubmissionId == incorrectSubmissionId);
        Assert.Contains(attempts, item => item.SubmissionId == acceptedSubmissionId);
        Assert.Contains(attempts, item => item.RejectionCode == "empty_flag");
        Assert.Contains(attempts, item => item.RejectionCode == "challenge_inaccessible");
        Assert.All(attempts, item => Assert.DoesNotContain(expectedFlag, item.ProtectedSubmittedFlag ?? "", StringComparison.Ordinal));

        var auditText = JsonSerializer.Serialize(await verifyDb.AuditEvents.AsNoTracking().ToArrayAsync());
        var systemText = string.Join('\n', await verifyDb.Logs.AsNoTracking()
            .Select(item => item.Message + item.Exception).ToArrayAsync());
        Assert.DoesNotContain(expectedFlag, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(incorrectFlag, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedFlag, systemText, StringComparison.Ordinal);
        Assert.DoesNotContain(incorrectFlag, systemText, StringComparison.Ordinal);
    }

    private async Task<HttpClient> LoginAsync(string userName, string password)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = userName, Password = password });
        login.EnsureSuccessStatusCode();
        return client;
    }
}
