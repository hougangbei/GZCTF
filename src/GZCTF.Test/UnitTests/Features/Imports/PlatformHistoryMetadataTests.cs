using System.Text.Json;
using GZCTF.Features.Imports.Application;
using Xunit;

namespace GZCTF.Test.UnitTests.Features.Imports;

public sealed class PlatformHistoryMetadataTests
{
    [Fact]
    public void Source_metadata_retains_safe_plugin_fields_but_omits_auth_tables_and_recursive_secrets()
    {
        const string input = """
            {"title":"public event","website":"https://user:credential-ten@example.test/path","plugin":{"difficulty":"expert","password_hash":"credential-one","nested":{"apiToken":"credential-two","nonce":"credential-three","hint":"safe","settings":[{"key":"api_token","value":"credential-nine"}]}},"tokens":[{"value":"credential-four"}],"sessions":[{"id":"credential-five"}],"tracking":[{"ip":"credential-six"}],"config":[{"key":"smtp_password","value":"credential-seven"}],"encoded":"{\"secret\":\"credential-eight\",\"help\":\"visible\"}"}
            """;
        var safe = PlatformHistoryService.SanitizeMetadata(input).GetRawText();
        Assert.Contains("expert", safe);
        Assert.Contains("safe", safe);
        Assert.Contains("visible", safe);
        Assert.DoesNotContain("credential-", safe);
        Assert.DoesNotContain("tokens", safe);
        Assert.DoesNotContain("tracking", safe);
        Assert.DoesNotContain("password_hash", safe);
    }

    [Fact]
    public void Ordinary_records_with_credential_words_in_their_names_and_presets_are_retained()
    {
        const string input = """{"id":1,"name":"Secret challenge","value":100,"preset":"beginner"}""";
        var safe = PlatformHistoryService.SanitizeMetadata(input);
        Assert.Equal("Secret challenge", safe.GetProperty("name").GetString());
        Assert.Equal("beginner", safe.GetProperty("preset").GetString());
    }

    [Fact]
    public void Malformed_or_opaque_metadata_is_not_retained_as_an_uninspected_blob()
    {
        Assert.Equal("{}", PlatformHistoryService.SanitizeMetadata("not-json credential").GetRawText());
        Assert.Equal("{}", PlatformHistoryService.SanitizeMetadata("\"credential\"").GetRawText());
    }
}
