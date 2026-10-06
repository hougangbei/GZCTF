using System;
using System.Text.Json;
using GZCTF.Features.AboutPage.Api;
using Xunit;

namespace GZCTF.Test.UnitTests.AboutPage;

public sealed class AboutDocumentValidatorTests
{
    [Fact]
    public void Normalize_RejectsUnsupportedSchemaVersion()
    {
        var result = AboutDocumentValidator.Normalize("{\"schemaVersion\":2,\"title\":\"实验室\",\"sections\":[]}");

        Assert.Equal("unsupported_schema_version", result.Error);
        Assert.Null(result.Json);
    }

    [Fact]
    public void Normalize_CleansDangerousHtmlAndKeepsAllowedText()
    {
        const string input = "{\"schemaVersion\":1,\"title\":\"实验室\",\"sections\":[{\"id\":\"s\",\"title\":\"介绍\",\"items\":[{\"id\":\"t\",\"type\":\"text\",\"width\":12,\"title\":\"内容\",\"html\":\"<p style='color:red'>安全文字</p><script>alert(1)</script><a href='javascript:alert(1)' onclick='alert(1)'>链接</a>\"}]}]}";

        var result = AboutDocumentValidator.Normalize(input);

        Assert.Null(result.Error);
        Assert.True(result.Changed);
        using var document = JsonDocument.Parse(result.Json!);
        var html = document.RootElement.GetProperty("sections")[0].GetProperty("items")[0].GetProperty("html").GetString();
        Assert.Contains("安全文字", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_RejectsNonGridWidths()
    {
        const string input = "{\"schemaVersion\":1,\"title\":\"实验室\",\"sections\":[{\"id\":\"s\",\"title\":\"介绍\",\"items\":[{\"id\":\"t\",\"type\":\"text\",\"width\":5,\"title\":\"内容\",\"html\":\"<p>x</p>\"}]}]}";

        var result = AboutDocumentValidator.Normalize(input);

        Assert.Equal("invalid_component_width", result.Error);
    }
}
