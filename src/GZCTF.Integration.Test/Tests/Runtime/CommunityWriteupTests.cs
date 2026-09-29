using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GZCTF.Features.ChallengeLibrary.Domain;
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
public sealed class CommunityWriteupTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Anonymous_pdf_submission_remains_private_until_admin_approves_it()
    {
        var challengeId = await SeedChallengeAsync();
        using var visitor = factory.CreateClient();
        using var form = PdfForm("First solution", "Visitor", "%PDF-1.4\nexample\n%%EOF");

        var created = await visitor.PostAsync($"/api/challenges/{challengeId}/community-writeups", form);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var writeupId = createdJson.RootElement.GetProperty("id").GetGuid();

        var publicList = await visitor.GetFromJsonAsync<JsonElement>($"/api/challenges/{challengeId}/community-writeups");
        Assert.Equal(0, publicList.GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound,
            (await visitor.GetAsync($"/api/challenges/{challengeId}/community-writeups/{writeupId}/pdf")).StatusCode);

        using var admin = await CreateAdminClientAsync();
        var pending = await admin.GetFromJsonAsync<JsonElement>("/api/admin/community-writeups?status=pending");
        Assert.Contains(pending.EnumerateArray(), item => item.GetProperty("id").GetGuid() == writeupId);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync($"/api/challenges/{challengeId}/community-writeups/{writeupId}/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await visitor.PostAsJsonAsync($"/api/admin/community-writeups/{writeupId}/review", new { approved = true })).StatusCode);

        var review = await admin.PostAsJsonAsync($"/api/admin/community-writeups/{writeupId}/review", new { approved = true });
        Assert.Equal(HttpStatusCode.NoContent, review.StatusCode);
        publicList = await visitor.GetFromJsonAsync<JsonElement>($"/api/challenges/{challengeId}/community-writeups");
        Assert.Single(publicList.EnumerateArray());
        var pdf = await visitor.GetAsync($"/api/challenges/{challengeId}/community-writeups/{writeupId}/pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF-", await pdf.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rejecting_one_pdf_does_not_hide_other_approved_writeups()
    {
        var challengeId = await SeedChallengeAsync();
        using var visitor = factory.CreateClient();
        var ids = new List<Guid>();
        foreach (var title in new[] { "Approved solution", "Rejected solution" })
        {
            using var form = PdfForm(title, "Contributor", "%PDF-1.5\nexample\n%%EOF");
            var response = await visitor.PostAsync($"/api/challenges/{challengeId}/community-writeups", form);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            ids.Add(json.RootElement.GetProperty("id").GetGuid());
        }

        using var admin = await CreateAdminClientAsync();
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"/api/admin/community-writeups/{ids[0]}/review", new { approved = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"/api/admin/community-writeups/{ids[1]}/review", new { approved = false })).StatusCode);

        var list = await visitor.GetFromJsonAsync<JsonElement>($"/api/challenges/{challengeId}/community-writeups");
        Assert.Equal(ids[0], Assert.Single(list.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound,
            (await visitor.GetAsync($"/api/challenges/{challengeId}/community-writeups/{ids[1]}/pdf")).StatusCode);
    }

    [Theory]
    [InlineData("solution.txt", "plain text")]
    [InlineData("solution.pdf", "plain text")]
    public async Task Submission_rejects_non_pdf_content(string fileName, string contents)
    {
        var challengeId = await SeedChallengeAsync();
        using var visitor = factory.CreateClient();
        using var form = PdfForm("Invalid", "Visitor", contents, fileName);

        var response = await visitor.PostAsync($"/api/challenges/{challengeId}/community-writeups", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static MultipartFormDataContent PdfForm(string title, string author, string contents, string fileName = "solution.pdf")
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(title), "title");
        form.Add(new StringContent(author), "authorName");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(contents)), "file", fileName);
        return form;
    }

    private async Task<Guid> SeedChallengeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var challenge = new CanonicalChallenge
        {
            Id = Guid.CreateVersion7(),
            Type = ChallengeType.StaticAttachment,
            PublicationState = ChallengePublicationState.Published,
            IsEnabled = true,
            SourceType = "native",
            SourceId = Guid.NewGuid().ToString("N"),
            Localizations = [new ChallengeLocalization { Locale = "en", Title = "WP challenge", Body = "Solve me" }]
        };
        db.Challenges.Add(challenge);
        await db.SaveChangesAsync();
        return challenge.Id;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        const string password = "Wp!AdminPassword2026";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password,
            role: Role.Admin);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = user.UserName, Password = password });
        login.EnsureSuccessStatusCode();
        return client;
    }
}
