using System.Net;
using System.Net.Http.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public class AdminPrivilegeBoundaryTests(GZCTFApplicationFactory factory)
{
    private static readonly string[] AdminReads =
    [
        "/api/admin/config",
        "/api/admin/skill-trees",
        "/api/admin/skill-categories",
        "/api/admin/challenges",
        "/api/admin/lessons",
        "/api/admin/cohorts",
        "/api/admin/community-writeups?status=pending",
        "/api/admin/audit-events",
        "/api/admin/flag-attempts",
        "/api/admin/qq-bot/settings",
        "/api/tokens"
    ];

    [Fact]
    public async Task AnonymousAndOrdinaryUserCannotReadOrWriteAdminResources()
    {
        const string password = "Boundary!User2026";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), password, role: Role.User);

        using var anonymous = factory.CreateClient();
        using var ordinary = factory.CreateClient();
        var login = await ordinary.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = user.UserName, Password = password });
        login.EnsureSuccessStatusCode();

        foreach (var path in AdminReads)
        {
            using var anonymousResponse = await anonymous.GetAsync(path);
            using var ordinaryResponse = await ordinary.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, ordinaryResponse.StatusCode);
        }

        foreach (var path in new[] { "/api/admin/skill-trees", "/api/admin/cohorts" })
        {
            using var anonymousResponse = await anonymous.PostAsJsonAsync(path, new { name = "Denied" });
            using var ordinaryResponse = await ordinary.PostAsJsonAsync(path, new { name = "Denied" });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, ordinaryResponse.StatusCode);
        }
    }
}
