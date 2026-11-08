using System.Net;
using System.Net.Http.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class OfflineUpdatePackagesTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Package_routes_are_admin_only_and_fail_closed_without_host_service()
    {
        using var anon = factory.CreateClient();
        using var learner = await Client(Role.User);
        using var admin = await Client(Role.Admin);
        foreach (var path in new[] { "/api/admin/updates/packages", "/api/admin/updates/packages/settings" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await learner.GetAsync(path)).StatusCode);
            var response = await admin.GetAsync(path);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
        var id = new string('a', 32);
        foreach (var path in new[] { $"/api/admin/updates/packages/{id}/validate", $"/api/admin/updates/packages/{id}/checksum" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync(path, new { sha256 = new string('b', 64) })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await learner.PostAsJsonAsync(path, new { sha256 = new string('b', 64) })).StatusCode);
        }
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/admin/updates/packages/upload")
            { Content = new ByteArrayContent([1, 2, 3]) };
        upload.Headers.Add("X-File-Name", "release.tar.gz");
        Assert.Equal(HttpStatusCode.Forbidden, (await learner.SendAsync(upload)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/updates/packages/{id}/apply",
            new { validationId = new string('b', 32), operationId = Guid.NewGuid(), maintenanceConfirmed = false })).StatusCode);
    }

    private async Task<HttpClient> Client(Role role)
    {
        const string password = "Package!TestLearner1";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password, role: role);
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/account/login", new LoginModel { UserName = user.UserName, Password = password })).EnsureSuccessStatusCode();
        return client;
    }
}
