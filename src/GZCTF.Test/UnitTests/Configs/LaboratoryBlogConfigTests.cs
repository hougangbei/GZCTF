using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using GZCTF.Models.Internal;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Test.UnitTests.Configs;

public class LaboratoryBlogConfigTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("https://blog.example.com/posts?q=ctf#intro", true)]
    [InlineData("http://localhost:8081/blog", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,blog", false)]
    [InlineData("ftp://blog.example.com", false)]
    [InlineData("//blog.example.com", false)]
    [InlineData("/blog", false)]
    [InlineData("https://", false)]
    [InlineData("https://blog.example.com/a b", false)]
    public void BlogUrl_OnlyAllowsOptionalAbsoluteHttpUrls(string? url, bool expected)
    {
        var json = JsonSerializer.Serialize(new { LaboratoryBlogUrl = url });
        var config = JsonSerializer.Deserialize<GlobalConfig>(json)!;
        var valid = Validator.TryValidateObject(config, new ValidationContext(config), [], true);

        Assert.Equal(expected, valid);
    }

    [Fact]
    public void PublicConfig_ExposesTheConfiguredBlogUrl()
    {
        var services = new ServiceCollection();
        services.Configure<GlobalConfig>(value => value.LaboratoryBlogUrl = "https://blog.example.com/");
        services.AddOptions<ContainerPolicy>();
        services.AddOptions<ContainerProvider>();
        services.AddOptions<ManagedConfig>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var config = ClientConfig.FromServiceProvider(scope.ServiceProvider);
        var json = JsonSerializer.SerializeToElement(config);

        Assert.True(json.TryGetProperty("LaboratoryBlogUrl", out var url));
        Assert.Equal("https://blog.example.com/", url.GetString());
    }
}
