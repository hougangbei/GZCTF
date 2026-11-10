using System.Net;
using System.Net.Http.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models.Internal;
using GZCTF.Models.Request.Account;
using GZCTF.Models.Request.Admin;
using GZCTF.Utils;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public class LaboratoryNetdiskSettingsTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task OnlyAdminCanSaveNetdiskUrl_AndPublicConfigRefreshesAfterSaveAndClear()
    {
        const string password = "Netdisk!Settings2026";
        var adminUser = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), password, role: Role.Admin);
        var ordinaryUser = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), password, role: Role.User);
        using var admin = factory.CreateClient();
        using var ordinary = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/account/login",
            new LoginModel { UserName = adminUser.UserName, Password = password })).EnsureSuccessStatusCode();
        (await ordinary.PostAsJsonAsync("/api/account/login",
            new LoginModel { UserName = ordinaryUser.UserName, Password = password })).EnsureSuccessStatusCode();
        var original = (await admin.GetFromJsonAsync<ConfigEditModel>("/api/admin/config"))!;
        var previousUrl = original.GlobalConfig!.LaboratoryNetdiskUrl;
        try
        {
            // Prime the public cache before changing the setting.
            (await anonymous.GetAsync("/api/config")).EnsureSuccessStatusCode();
            original.GlobalConfig.LaboratoryNetdiskUrl = "https://pan.example.com/laboratory";
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await anonymous.PutAsJsonAsync("/api/admin/config", original)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await ordinary.PutAsJsonAsync("/api/admin/config", original)).StatusCode);
            (await admin.PutAsJsonAsync("/api/admin/config", original)).EnsureSuccessStatusCode();
            Assert.Equal(original.GlobalConfig.LaboratoryNetdiskUrl,
                (await admin.GetFromJsonAsync<ConfigEditModel>("/api/admin/config"))!.GlobalConfig!.LaboratoryNetdiskUrl);
            Assert.Equal(original.GlobalConfig.LaboratoryNetdiskUrl,
                (await anonymous.GetFromJsonAsync<ClientConfig>("/api/config"))!.LaboratoryNetdiskUrl);

            foreach (var invalid in new[] { "javascript:alert(1)", "ftp://pan.example.com", "/blog" })
            {
                original.GlobalConfig.LaboratoryNetdiskUrl = invalid;
                Assert.Equal(HttpStatusCode.BadRequest,
                    (await admin.PutAsJsonAsync("/api/admin/config", original)).StatusCode);
                Assert.Equal("https://pan.example.com/laboratory",
                    (await anonymous.GetFromJsonAsync<ClientConfig>("/api/config"))!.LaboratoryNetdiskUrl);
            }

            original.GlobalConfig.LaboratoryNetdiskUrl = "";
            (await admin.PutAsJsonAsync("/api/admin/config", original)).EnsureSuccessStatusCode();
            Assert.True(string.IsNullOrEmpty(
                (await anonymous.GetFromJsonAsync<ClientConfig>("/api/config"))!.LaboratoryNetdiskUrl));
        }
        finally
        {
            original.GlobalConfig.LaboratoryNetdiskUrl = previousUrl;
            (await admin.PutAsJsonAsync("/api/admin/config", original)).EnsureSuccessStatusCode();
        }
    }
}
