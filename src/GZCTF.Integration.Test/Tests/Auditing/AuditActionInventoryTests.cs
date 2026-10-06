using System.Reflection;
using GZCTF.Features.Auditing.Application;
using GZCTF.Middlewares;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Auditing;

public sealed class AuditActionInventoryTests
{
    [Fact]
    public void Every_admin_write_action_is_catalogued_and_draft_get_is_explicitly_catalogued()
    {
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type)).ToArray();
        var missing = new List<string>();

        foreach (var controller in controllers)
        {
            var isAdmin = controller.GetCustomAttributes(true).Any(attribute =>
                attribute is RequireAdminAttribute or RequireAdminOrTokenAttribute);
            foreach (var method in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                var isAdminAction = isAdmin || method.GetCustomAttributes(true).Any(attribute =>
                    attribute is RequireAdminAttribute or RequireAdminOrTokenAttribute);
                if (!isAdminAction) continue;
                var httpMethods = method.GetCustomAttributes(true).OfType<HttpMethodAttribute>().ToArray();
                if (!httpMethods.Any(http => http.HttpMethods.Any(verb => verb is "POST" or "PUT" or "PATCH" or "DELETE")))
                    continue;

                var audit = method.GetCustomAttribute<AuditActionAttribute>();
                var excluded = method.GetCustomAttribute<AuditExcludedAttribute>();
                if (excluded is not null)
                {
                    Assert.Equal("AdminController.SearchUsers", $"{controller.Name}.{method.Name}");
                    Assert.Equal("Read-only user search", excluded.Reason);
                    continue;
                }
                if (audit is null)
                {
                    missing.Add($"{controller.Name}.{method.Name}");
                    continue;
                }
                Assert.Contains(AuditActionCatalog.All, item => item.Code == audit.Code);
            }
        }

        Assert.Empty(missing);

        var draftGet = controllers.SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Single(method => method.Name == "GetDraft");
        Assert.Equal("skill_trees.draft.get", draftGet.GetCustomAttribute<AuditActionAttribute>()?.Code);
    }
}
