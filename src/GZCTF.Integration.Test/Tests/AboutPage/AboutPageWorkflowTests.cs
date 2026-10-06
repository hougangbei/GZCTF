using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Features.AboutPage.Domain;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.AboutPage;

[Collection(nameof(IntegrationTestCollection))]
public sealed class AboutPageWorkflowTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Draft_lock_publish_and_restore_keep_public_version_isolated()
    {
        await ResetStateAsync();
        using var firstAdmin = await CreateAdminAsync();
        using var secondAdmin = await CreateAdminAsync();

        using (var unpublished = await factory.CreateClient().GetAsync("/api/about"))
        {
            unpublished.EnsureSuccessStatusCode();
            Assert.Contains("\"published\":false", await unpublished.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Assert.Equal(HttpStatusCode.OK, (await firstAdmin.PostAsync("/api/admin/about/lock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await secondAdmin.PostAsync("/api/admin/about/lock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await secondAdmin.DeleteAsync("/api/admin/about/lock")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await firstAdmin.PostAsJsonAsync("/api/admin/about/publish", new { expectedRevision = 0 })).StatusCode);

        const long initialRevision = 0;
        var firstDocument = Document("内部草稿一");
        var saveFirst = await firstAdmin.PutAsJsonAsync("/api/admin/about/draft", new
        {
            document = firstDocument,
            expectedRevision = initialRevision
        });
        saveFirst.EnsureSuccessStatusCode();
        var savedFirst = await saveFirst.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(1, savedFirst!.RootElement.GetProperty("revision").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict, (await firstAdmin.PutAsJsonAsync("/api/admin/about/draft", new
        {
            document = Document("过期草稿"), expectedRevision = initialRevision
        })).StatusCode);

        using (var stillUnpublished = await factory.CreateClient().GetAsync("/api/about"))
        {
            var publicJson = await stillUnpublished.Content.ReadAsStringAsync();
            Assert.Contains("\"published\":false", publicJson, StringComparison.Ordinal);
            Assert.DoesNotContain("内部草稿一", publicJson, StringComparison.Ordinal);
        }

        var publishFirst = await firstAdmin.PostAsJsonAsync("/api/admin/about/publish", new { expectedRevision = 1 });
        publishFirst.EnsureSuccessStatusCode();
        var firstVersion = await publishFirst.Content.ReadFromJsonAsync<JsonDocument>();
        var firstVersionId = firstVersion!.RootElement.GetProperty("id").GetGuid();

        var saveSecond = await firstAdmin.PutAsJsonAsync("/api/admin/about/draft", new
        {
            document = Document("公开版本二"), expectedRevision = 1
        });
        saveSecond.EnsureSuccessStatusCode();
        (await firstAdmin.PostAsJsonAsync("/api/admin/about/publish", new { expectedRevision = 2 })).EnsureSuccessStatusCode();

        using (var currentPublic = await factory.CreateClient().GetAsync("/api/about"))
        {
            currentPublic.EnsureSuccessStatusCode();
            var publicJson = await currentPublic.Content.ReadAsStringAsync();
            using var publicDocument = JsonDocument.Parse(publicJson);
            Assert.Equal("公开版本二", publicDocument.RootElement.GetProperty("document").GetProperty("title").GetString());
        }

        var restore = await firstAdmin.PostAsJsonAsync($"/api/admin/about/versions/{firstVersionId}/restore", new { expectedRevision = 2 });
        restore.EnsureSuccessStatusCode();
        var restoredState = await firstAdmin.GetFromJsonAsync<JsonDocument>("/api/admin/about");
        using var restoredDocument = JsonDocument.Parse(restoredState!.RootElement.GetProperty("document").GetString()!);
        Assert.Equal("内部草稿一", restoredDocument.RootElement.GetProperty("title").GetString());
        using (var publicAfterRestore = await factory.CreateClient().GetAsync("/api/about"))
        {
            using var publicJson = JsonDocument.Parse(await publicAfterRestore.Content.ReadAsStringAsync());
            Assert.Equal("公开版本二", publicJson.RootElement.GetProperty("document").GetProperty("title").GetString());
        }

        Assert.Equal(HttpStatusCode.OK, (await firstAdmin.DeleteAsync("/api/admin/about/lock")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secondAdmin.PostAsync("/api/admin/about/lock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secondAdmin.DeleteAsync("/api/admin/about/lock")).StatusCode);
    }

    private async Task ResetStateAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var state = await db.AboutPageStates.SingleAsync(item => item.Id == 1);
        state.CurrentVersionId = null;
        state.LockOwnerId = null;
        state.LockOwnerName = null;
        state.LockCreatedAtUtc = null;
        state.DraftRevision = 0;
        state.DraftJson = EmptyDocument();
        db.AboutPageVersions.RemoveRange(await db.AboutPageVersions.ToArrayAsync());
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> CreateAdminAsync()
    {
        const string password = "About!WorkflowAdmin1";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password,
            role: Role.Admin);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/Account/LogIn", new LoginModel
        {
            UserName = user.UserName,
            Password = password
        });
        login.EnsureSuccessStatusCode();
        return client;
    }

    private static string Document(string title) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        title,
        sections = new[]
        {
            new
            {
                id = "intro",
                title = "实验室介绍",
                items = new[]
                {
                    new { id = "paragraph", type = "text", width = 12, title = "正文", html = $"<p>{title}</p>" }
                }
            }
        }
    });

    private static string EmptyDocument() => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        title = "实验室关于页",
        sections = Array.Empty<object>()
    });
}
