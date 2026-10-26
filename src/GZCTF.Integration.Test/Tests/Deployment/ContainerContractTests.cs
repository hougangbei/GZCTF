using System.Reflection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Deployment;

public class ContainerContractTests
{
    [Fact]
    public void Dockerfile_exposes_runtime_contract()
    {
        var dockerfile = File.ReadAllText(FindRepoFile("src/GZCTF/Dockerfile"));

        Assert.Contains("EXPOSE 8080", dockerfile);
        Assert.Contains("http://localhost:3000/healthz", dockerfile);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"GZCTF.dll\"]", dockerfile);
    }

    [Fact]
    public void Server_and_application_keep_environment_contract()
    {
        var server = File.ReadAllText(FindRepoFile("src/GZCTF/Server.cs"));
        var appBuilder = File.ReadAllText(FindRepoFile("src/GZCTF/Extensions/Startup/AppBuilderExtensions.cs"));

        Assert.Contains("Environment.GetEnvironmentVariable(\"GZCTF_METRIC_PORT\")", server);
        Assert.Contains("Environment.GetEnvironmentVariable(\"GZCTF_SERVER_PORT\")", server);
        AssertConfiguredPort("MetricPort", "GZCTF_METRIC_PORT", 3000);
        AssertConfiguredPort("ServerPort", "GZCTF_SERVER_PORT", 8080);
        Assert.Contains("AddEnvironmentVariables(\"GZCTF_\")", appBuilder);
    }

    [Theory]
    [InlineData(null, 3000, 3000)]
    [InlineData("", 8080, 8080)]
    [InlineData("42001", 3000, 42001)]
    [InlineData("65535", 8080, 65535)]
    [InlineData("0", 3000, 3000)]
    [InlineData("65536", 8080, 8080)]
    [InlineData("invalid", 3000, 3000)]
    public void Port_environment_values_use_valid_overrides_and_otherwise_keep_the_default(
        string? value, int fallback, int expected) => Assert.Equal(expected, ParsePort(value, fallback));

    private static void AssertConfiguredPort(string field, string environmentVariable, int fallback)
    {
        var value = typeof(Server).GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        Assert.Equal(ParsePort(Environment.GetEnvironmentVariable(environmentVariable), fallback), value);
    }

    private static int ParsePort(string? value, int fallback) =>
        (int)typeof(Server).GetMethod("ParsePort", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [value, fallback])!;

    private static string FindRepoFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }
}
