using GZCTF.Features.Updates.Application;
using Xunit;

namespace GZCTF.Test.UnitTests.Features.Updates;

public sealed class UpdateSourceSettingsTests
{
    [Fact]
    public void Defaults_to_project_github_main()
    {
        var source = new UpdateSourceSettings();
        Assert.Equal("github", source.Kind);
        Assert.Equal("https://github.com/hougangbei/GZCTF", source.Location);
        Assert.Equal("main", source.Ref);
    }

    [Fact]
    public void Normalization_preserves_saved_generation_for_validation_binding()
    {
        var source = new UpdateSourceSettings { Generation = "0123456789abcdef0123456789abcdef" };
        Assert.True(UpdateSourceSettings.TryNormalize(source, out var normalized));
        Assert.Equal(source.Generation, normalized.Generation);
        var json = System.Text.Json.JsonSerializer.Serialize(normalized);
        Assert.Contains("\"generation\":\"0123456789abcdef0123456789abcdef\"", json);
    }

    [Theory]
    [InlineData("not-a-generation")]
    [InlineData("0123456789abcdef0123456789abcdeF")]
    public void Rejects_invalid_persisted_generation(string generation) =>
        Assert.False(UpdateSourceSettings.TryNormalize(new() { Generation = generation }, out _));

    [Theory]
    [InlineData("https://github.com/owner/repo.git/", "main", "https://github.com/owner/repo", "main")]
    [InlineData("https://github.com/owner/repo/tree/feature/update", "main", "https://github.com/owner/repo", "feature/update")]
    public void Canonicalizes_github_location(string location, string branch, string expected, string expectedRef)
    {
        var source = new UpdateSourceSettings { Location = location, Ref = branch };
        Assert.True(UpdateSourceSettings.TryNormalize(source, out var normalized));
        Assert.Equal(expected, normalized.Location);
        Assert.Equal(expectedRef, normalized.Ref);
    }

    [Theory]
    [InlineData("github", "https://evil.test/owner/repo", "main", "")]
    [InlineData("github", "https://user:secret@github.com/owner/repo", "main", "")]
    [InlineData("github", "https://github.com/owner/repo?token=secret", "main", "")]
    [InlineData("github", "https://github.com/owner/repo#main", "main", "")]
    [InlineData("github", "https://github.com/owner/repo", "--upload-pack=evil", "")]
    [InlineData("github", "https://github.com/owner/repo", "a..b", "")]
    [InlineData("directory", "relative/path", "", "")]
    [InlineData("directory", "/tmp/project", "main", "")]
    [InlineData("package", "file:///tmp/package.zip", "", "")]
    [InlineData("package", "/tmp/package.zip", "", "")]
    [InlineData("package", "https://example.test/package.zip#secret", "", "")]
    [InlineData("package", "https://example.test/package.zip", "", "bad")]
    [InlineData("unknown", "/tmp/project", "", "")]
    public void Rejects_unsafe_or_ambiguous_sources(string kind, string location, string branch, string sha256)
    {
        Assert.False(UpdateSourceSettings.TryNormalize(new() { Kind = kind, Location = location,
            Ref = branch, Sha256 = sha256 }, out _));
    }

    [Theory]
    [InlineData("directory", "/tmp/project/../release/", "/tmp/release")]
    [InlineData("package", "https://example.test/release.zip?download=1", "https://example.test/release.zip?download=1")]
    public void Accepts_local_sources_and_package_urls(string kind, string location, string expected)
    {
        Assert.True(UpdateSourceSettings.TryNormalize(new() { Kind = kind, Location = location, Ref = "" }, out var source));
        Assert.Equal(expected, source.Location);
    }

    [Theory]
    [InlineData(null, 8080)]
    [InlineData("", 8080)]
    [InlineData("0", 8080)]
    [InlineData("65536", 8080)]
    [InlineData("invalid", 8080)]
    [InlineData("19321", 19321)]
    public void Configurable_ports_have_safe_fallback(string? value, int expected) =>
        Assert.Equal(expected, Server.ParsePort(value, 8080));
}
