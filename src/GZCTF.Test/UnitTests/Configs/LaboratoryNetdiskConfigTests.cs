using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using GZCTF.Models.Internal;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Test.UnitTests.Configs;

public class LaboratoryNetdiskConfigTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("https://pan.example.com/posts?q=ctf#intro", true)]
    [InlineData("http://localhost:8081/blog", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,blog", false)]
    [InlineData("ftp://pan.example.com", false)]
    [InlineData("//pan.example.com", false)]
    [InlineData("/blog", false)]
    [InlineData("https://", false)]
    [InlineData("https://pan.example.com/a b", false)]
    public void NetdiskUrl_OnlyAllowsOptionalAbsoluteHttpUrls(string? url, bool expected)
    {
        var json = JsonSerializer.Serialize(new { LaboratoryNetdiskUrl = url });
        var config = JsonSerializer.Deserialize<GlobalConfig>(json)!;
        var valid = Validator.TryValidateObject(config, new ValidationContext(config), [], true);

        Assert.Equal(expected, valid);
    }

    [Fact]
    public void PublicConfig_ExposesTheConfiguredNetdiskUrl()
    {
        var services = new ServiceCollection();
        services.Configure<GlobalConfig>(value => value.LaboratoryNetdiskUrl = "https://pan.example.com/");
        services.AddOptions<ContainerPolicy>();
        services.AddOptions<ContainerProvider>();
        services.AddOptions<ManagedConfig>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var config = ClientConfig.FromServiceProvider(scope.ServiceProvider);
        var json = JsonSerializer.SerializeToElement(config);

        Assert.True(json.TryGetProperty("LaboratoryNetdiskUrl", out var url));
        Assert.Equal("https://pan.example.com/", url.GetString());
    }
}
