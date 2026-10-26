using System.Net;
using System.Net.Http.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Utils;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Account;
using GZCTF.Models.Request.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Accounts;

[Collection(nameof(IntegrationTestCollection))]
public sealed class AdminDelegationTests(GZCTFApplicationFactory factory)
{
    private const string Password = "Delegation!2026";
    private async Task<HttpClient> Login(string name, string password = Password)
    {
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/account/login", new LoginModel { UserName = name, Password = password }))
            .EnsureSuccessStatusCode();
        return client;
    }
    private async Task<UserInfo> Read(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    [Fact]
    public async Task Delegated_admin_cannot_grant_Admin_even_with_a_direct_request_or_other_profile_changes()
    {
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Admin);
        var target = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password);
        using var client = await Login(admin.UserName);
        var response = await client.PutAsJsonAsync($"/api/admin/users/{target.Id}",
            new AdminUserInfoModel { Role = Role.Admin, Bio = "must not persist", Email = "forbidden@example.test" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var unchanged = await Read(target.Id);
        Assert.Equal(Role.User, unchanged.Role);
        Assert.NotEqual("must not persist", unchanged.Bio);
        Assert.Equal(target.Email, unchanged.Email);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/users/{admin.Id}",
            new AdminUserInfoModel { Role = Role.Admin, UserName = "Admin" })).StatusCode);
        // Ordinary user management is retained.
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/admin/users/{target.Id}",
            new AdminUserInfoModel { Role = Role.Monitor, Bio = "allowed" })).StatusCode);
        Assert.Equal(Role.Monitor, (await Read(target.Id)).Role);
    }

    [Fact]
    public async Task Only_builtin_Admin_can_promote_and_revoke_administrators()
    {
        var target = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password);
        using var owner = await Login("Admin", "Admin@2022");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/admin/users/{target.Id}", new AdminUserInfoModel { Role = Role.Admin })).StatusCode);
        Assert.Equal(Role.Admin, (await Read(target.Id)).Role);
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Admin);
        using var delegated = await Login(admin.UserName);
        Assert.Equal(HttpStatusCode.Forbidden, (await delegated.PutAsJsonAsync($"/api/admin/users/{target.Id}", new AdminUserInfoModel { Role = Role.User })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/admin/users/{target.Id}", new AdminUserInfoModel { Role = Role.User })).StatusCode);
        Assert.Equal(Role.User, (await Read(target.Id)).Role);
    }

    [Fact]
    public async Task Delegated_admin_cannot_take_over_or_delete_other_admins_through_alternate_endpoints()
    {
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Admin);
        var other = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Admin);
        using var client = await Login(admin.UserName);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/admin/users/{other.Id}/password")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/admin/users/{other.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/users/{other.Id}", new AdminUserInfoModel { Email = "takeover@example.test" })).StatusCode);
        var freshName = TestDataSeeder.RandomName();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/admin/users", new[] {
            new UserCreateModel { UserName = freshName, Email = freshName + "@example.test", Password = Password },
            new UserCreateModel { UserName = other.UserName, Email = other.Email, Password = "Changed!2026" }
        })).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync(u => u.UserName == freshName));
        Assert.Equal(other.Email, (await Read(other.Id)).Email);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Undefined_numeric_roles_cannot_grant_privileges(bool quoted)
    {
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Admin);
        var target = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password);
        using var client = await Login(admin.UserName);
        var value = quoted ? "\"255\"" : "255";
        using var content = new StringContent("{\"role\":" + value + ",\"bio\":\"must not persist\"}", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync($"/api/admin/users/{target.Id}", content)).StatusCode);
        var actual = await Read(target.Id);
        Assert.Equal(Role.User, actual.Role);
        Assert.NotEqual("must not persist", actual.Bio);
    }

    [Fact]
    public async Task Batch_email_alias_cannot_reset_Admin_and_normal_user_creation_still_works()
    {
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Admin);
        using var client = await Login(admin.UserName);
        using var scope = factory.Services.CreateScope();
        var owner = (await scope.ServiceProvider.GetRequiredService<UserManager<UserInfo>>().FindByNameAsync("Admin"))!;
        var alias = TestDataSeeder.RandomName();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/admin/users", new[] {
            new UserCreateModel { UserName = alias, Email = owner.Email!, Password = "Changed!2026" }
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/admin/users/{owner.Id}/password")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/admin/users/{owner.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/users", new[] {
            new UserCreateModel { UserName = alias, Email = alias + "@example.test", Password = Password }
        })).StatusCode);
        using var learner = await Login(alias);
        Assert.Equal(HttpStatusCode.Forbidden, (await learner.PutAsJsonAsync($"/api/admin/users/{admin.Id}", new AdminUserInfoModel { Role = Role.Admin })).StatusCode);
        using var ownerClient = await Login("Admin", "Admin@2022");
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.DeleteAsync($"/api/admin/users/{admin.Id}/password")).StatusCode);
    }

    [Fact]
    public async Task Builtin_owner_cannot_be_renamed_or_demoted_through_admin_or_self_service()
    {
        using var client = await Login("Admin", "Admin@2022");
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<UserInfo>>();
        var owner = (await manager.FindByNameAsync("Admin"))!;
        try
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/users/{owner.Id}", new AdminUserInfoModel { UserName = "RenamedOwner" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/users/{owner.Id}", new AdminUserInfoModel { Role = Role.User })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/account/update", new ProfileUpdateModel { UserName = "RenamedOwner" })).StatusCode);
        }
        finally
        {
            using var restoreScope = factory.Services.CreateScope();
            var restore = restoreScope.ServiceProvider.GetRequiredService<UserManager<UserInfo>>();
            var actual = (await restore.FindByIdAsync(owner.Id.ToString()))!;
            await restore.SetUserNameAsync(actual, "Admin");
            actual.Role = Role.Admin;
            await restore.UpdateAsync(actual);
        }
    }
}
