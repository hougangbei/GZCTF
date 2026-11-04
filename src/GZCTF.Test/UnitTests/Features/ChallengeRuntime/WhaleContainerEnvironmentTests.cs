using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GZCTF.Models.Internal;
using GZCTF.Services.Container.Manager;
using Xunit;

namespace GZCTF.Test.UnitTests.Features.ChallengeRuntime;

public sealed class WhaleContainerEnvironmentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Docker_preserves_native_flag_and_only_adds_Whale_alias_when_requested(bool whale)
    {
        var config = new ContainerConfig { Flag = "fixture-flag" };
        var property = typeof(ContainerConfig).GetProperty("UseWhaleFlagEnvironment");
        Assert.NotNull(property);
        property.SetValue(config, whale);
        var env = (IList<string>)typeof(DockerManager).GetMethod("BuildContainerEnv", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [config])!;
        Assert.Contains("GZCTF_FLAG=fixture-flag", env);
        Assert.Equal(whale, env.Contains("FLAG=fixture-flag"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Kubernetes_preserves_native_flag_and_only_adds_Whale_alias_when_requested(bool whale)
    {
        var config = new ContainerConfig { Flag = "fixture-flag" };
        var property = typeof(ContainerConfig).GetProperty("UseWhaleFlagEnvironment");
        Assert.NotNull(property);
        property.SetValue(config, whale);
        var env = (IList<k8s.Models.V1EnvVar>)typeof(KubernetesManager).GetMethod("BuildContainerEnv", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [config])!;
        Assert.Contains(env, item => item.Name == "GZCTF_FLAG" && item.Value == "fixture-flag");
        Assert.Equal(whale, env.Any(item => item.Name == "FLAG" && item.Value == "fixture-flag"));
    }
}
