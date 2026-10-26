using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using GZCTF.Controllers;
using GZCTF.Repositories.Interface;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Test.UnitTests.Controllers;

public sealed class AdminDependencyIsolationTests
{
    [Fact]
    public void Admin_controller_can_be_activated_when_challenge_runtime_is_unavailable()
    {
        var services = new ServiceCollection();
        var runtimeResolutions = 0;
        services.AddScoped<IContainerRepository>(_ =>
        {
            runtimeResolutions++;
            throw new InvalidOperationException("Challenge Docker socket is unavailable");
        });
        // Supply inert non-runtime dependencies: this test checks MVC controller
        // activation, before any database or settings action is invoked.
        foreach (var parameter in typeof(AdminController).GetConstructors()[0].GetParameters())
        {
            var type = parameter.ParameterType;
            if (type == typeof(IContainerRepository) || type == typeof(IServiceProvider)) continue;
            services.AddSingleton(type, type.IsInterface
                ? DispatchProxy.Create(type, typeof(InertDependency))
                : RuntimeHelpers.GetUninitializedObject(type));
        }
        using var provider = services.BuildServiceProvider();
        var controller = ActivatorUtilities.CreateInstance<AdminController>(provider);
        Assert.NotNull(controller);
        Assert.Equal(0, runtimeResolutions);
        // The runtime remains unavailable; activation has not swallowed its error.
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IContainerRepository>());
        Assert.Equal(1, runtimeResolutions);
    }

    public class InertDependency : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            throw new InvalidOperationException("Unrelated dependency should not be called during controller activation");
    }
}
