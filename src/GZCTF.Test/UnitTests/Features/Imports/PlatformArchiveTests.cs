using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GZCTF.Features.Imports.Infrastructure;
using GZCTF.Utils;
using Xunit;

namespace GZCTF.Test.UnitTests.Features.Imports;

public sealed class PlatformArchiveTests
{
    [Fact]
    public async Task Gzctf_preserves_flags_assets_container_modes_and_archive_fingerprint()
    {
        var asset = Encoding.UTF8.GetBytes("synthetic attachment");
        var hash = Convert.ToHexStringLower(SHA256.HashData(asset));
        using var zip = Zip(new Dictionary<string, byte[]>
        {
            ["manifest.json"] = Bytes("""{"format":"GZCTF-GAME","version":"1.0"}"""),
            ["game.json"] = Bytes("""{"title":"Fixture game"}"""),
            ["challenges/1.json"] = Bytes($$$"""{"id":1,"title":"Static","content":"![asset](/assets/{{{hash}}})","category":"Web","type":"StaticAttachment","enabled":true,"scoring":{"original":100},"flags":{"static":[{"value":"fixture-flag"}]},"attachment":{"type":"Local","hash":"{{{hash}}}","fileName":"asset.txt"}}"""),
            ["challenges/2.json"] = Bytes("""{"id":2,"title":"Dynamic","category":"Pwn","type":"DynamicContainer","flags":{"template":null},"container":{"image":"fixture/image","exposePort":8080,"cpuCount":1,"memoryLimit":64,"storageLimit":256,"networkMode":32}}"""),
            [$"files/{hash}"] = asset
        });
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(zip.ToArray()));
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.Equal("GZCTF-GAME", package.Format);
        Assert.Equal("Fixture game", package.Title);
        Assert.Single(package.Files);
        Assert.Equal(fingerprint, package.Fingerprint);
        Assert.Equal("fixture-flag", package.Challenges[0].Flags.Single().Value);
        Assert.Equal(hash, package.Challenges[0].Attachments.Single().Sha256);
        Assert.Equal("gzctf-random", package.Challenges[1].Container!.FlagMode);
        Assert.Equal("isolated", package.Challenges[1].Container!.NetworkMode);
        Assert.DoesNotContain(package.Issues, issue => issue.Severity == "error");
    }

    [Fact]
    public async Task Ctfd_preserves_multiple_files_accounts_history_and_deduplicates_solves()
    {
        using var zip = Ctfd(new Dictionary<string, string>
        {
            ["challenges"] = """[{"id":1,"name":"Fixture","description":"Body","category":"Web","type":"dynamic","value":300,"state":"visible"}]""",
            ["flags"] = """[{"id":1,"challenge_id":1,"type":"static","content":"fixture","data":"case_insensitive"}]""",
            ["files"] = """[{"id":1,"challenge_id":1,"location":"folder/one.txt"},{"id":2,"challenge_id":1,"location":"folder/two.txt"}]""",
            ["users"] = """[{"id":4,"name":"fixture-user","email":"fixture@example.invalid","password":"fixture-private-hash","team_id":9,"verified":true}]""",
            ["teams"] = """[{"id":9,"name":"Fixture team"}]""",
            ["submissions"] = """[{"id":2,"user_id":4,"team_id":9,"challenge_id":1,"type":"correct","date":"2024-01-02T03:04:05Z","value":125},{"id":3,"user_id":4,"challenge_id":1,"type":"incorrect","date":"2024-01-02T03:05:05Z"}]""",
            ["solves"] = """[{"id":2,"user_id":4,"team_id":9,"challenge_id":1,"date":"2024-01-02T03:04:05Z","value":125}]""",
            ["config"] = """[{"key":"ctf_name","value":"Fixture CTF"}]"""
        }, new() { ["uploads/folder/one.txt"] = Bytes("one"), ["uploads/folder/two.txt"] = Bytes("two") });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.Equal("Fixture CTF", package.Title);
        Assert.Equal(ChallengeType.StaticAttachment, package.Challenges.Single().Type);
        Assert.Equal(2, package.Challenges.Single().Attachments.Count);
        Assert.True(package.Challenges.Single().Flags.Single().IgnoreCase);
        Assert.Equal("fixture-private-hash", package.Users.Single().PasswordHash);
        Assert.Equal(2, package.Submissions.Count);
        Assert.Equal(125, package.Submissions.Single(s => s.SourceId == "2").Score);
        Assert.Null(package.Submissions.Single(s => s.SourceId == "3").Score);
        Assert.DoesNotContain(package.Issues, issue => issue.Severity == "error");
    }

    [Fact]
    public async Task Unsupported_flags_and_plugins_and_missing_files_are_actionable_errors_without_secrets()
    {
        using var zip = Ctfd(new()
        {
            ["challenges"] = """[{"id":1,"name":"Fixture","type":"unknown-plugin","value":100}]""",
            ["flags"] = """[{"id":1,"challenge_id":1,"type":"regex","content":"private-secret"}]""",
            ["files"] = """[{"id":1,"challenge_id":1,"location":"missing.txt"}]"""
        });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.Contains(package.Issues, issue => issue.Code == "unsupported_challenge_type" && issue.Severity == "error");
        Assert.Contains(package.Issues, issue => issue.Code == "unsupported_flag_type" && issue.Severity == "error");
        Assert.Contains(package.Issues, issue => issue.Code == "missing_attachment" && issue.Severity == "error");
        Assert.All(package.Issues, issue => Assert.DoesNotContain("private-secret", issue.Message));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("C:/absolute")]
    [InlineData("uploads\\escape")]
    [InlineData("uploads/../escape")]
    public async Task Unsafe_archive_paths_are_rejected(string path)
    {
        using var zip = Zip(new() { [path] = Bytes("fixture") });
        await Assert.ThrowsAsync<InvalidDataException>(() => new PlatformArchiveReader().ReadAsync(zip));
    }

    [Fact]
    public async Task Duplicate_archive_entries_are_rejected()
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, true))
        {
            archive.CreateEntry("same.json");
            archive.CreateEntry("same.json");
        }
        zip.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => new PlatformArchiveReader().ReadAsync(zip));
    }

    [Fact]
    public async Task Malformed_json_and_duplicate_ids_are_rejected_as_issues()
    {
        using var zip = Ctfd(new()
        {
            ["challenges"] = """[{"id":1,"name":"One","type":"standard"},{"id":1,"name":"Two","type":"standard"}]""",
            ["users"] = """[{"id":1,"name":"One"},{"id":1,"name":"Two"}]""",
            ["flags"] = "[]"
        });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.Contains(package.Issues, issue => issue.Code == "duplicate_source_id");
        Assert.Contains(package.Issues, issue => issue.Code == "missing_flags_disabled");
    }

    [Fact]
    public async Task Ctfd_verifies_stored_upload_checksums_and_retains_body_reference()
    {
        using var zip = Ctfd(new()
        {
            ["challenges"] = """[{"id":1,"name":"Fixture","description":"![body](/files/folder/body.png)","type":"standard","value":100}]""",
            ["flags"] = """[{"id":1,"challenge_id":1,"type":"static","content":"fixture"}]""",
            ["files"] = """[{"id":1,"challenge_id":1,"location":"folder/body.png","sha1sum":"0000000000000000000000000000000000000000"}]"""
        }, new() { ["uploads/folder/body.png"] = Bytes("body asset") });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.Contains(package.Issues, issue => issue.Code == "attachment_hash_mismatch");
        Assert.Equal("/files/folder/body.png", package.Challenges.Single().Attachments.Single().OriginalUrl);
    }

    [Fact]
    public async Task Malformed_ctfd_table_becomes_actionable_issue()
    {
        using var zip = Zip(new()
        {
            ["db/challenges.json"] = Bytes("{broken-json")
        });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.Contains(package.Issues, issue => issue.Code == "malformed_table" && issue.Severity == "error");
    }

    [Fact]
    public async Task Archive_entry_count_is_bounded()
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, true))
            for (var i = 0; i <= PlatformArchiveReader.MaxFiles; i++)
                archive.CreateEntry($"entries/{i}");
        zip.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => new PlatformArchiveReader().ReadAsync(zip));
    }

    [Fact]
    public async Task Advertised_expanded_size_is_bounded_before_allocating_entry()
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, true))
        {
            using var output = archive.CreateEntry("oversized", CompressionLevel.Fastest).Open();
            var zeros = new byte[1024 * 1024];
            for (long written = 0; written <= PlatformArchiveReader.MaxExpandedBytes; written += zeros.Length)
                output.Write(zeros);
        }
        zip.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => new PlatformArchiveReader().ReadAsync(zip));
    }

    [Fact]
    public async Task Absolute_body_asset_urls_are_preserved_for_exact_rewriting_without_fetching()
    {
        var asset = Bytes("fixture body asset");
        var hash = Convert.ToHexStringLower(SHA256.HashData(asset));
        var original = "https://fixture.example.invalid/api/assets/" + hash;
        using var zip = Zip(new()
        {
            ["manifest.json"] = Bytes("""{"format":"GZCTF-GAME","version":"1.0"}"""),
            ["game.json"] = Bytes("""{"title":"Fixture"}"""),
            ["challenges/1.json"] = Bytes(System.Text.Json.JsonSerializer.Serialize(new
            {
                id = 1,
                title = "Fixture",
                type = "StaticAttachment",
                content = $"![asset]({original})",
                flags = new { @static = new[] { new { value = "fixture" } } }
            })),
            [$"files/{hash}"] = asset
        });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.Equal(original, package.Challenges.Single().Attachments.Single().OriginalUrl);
    }

    [Fact]
    public async Task Gzctf_preserves_template_and_per_flag_dynamic_attachments()
    {
        var one = Bytes("one");
        var two = Bytes("two");
        var h1 = Convert.ToHexStringLower(SHA256.HashData(one));
        var h2 = Convert.ToHexStringLower(SHA256.HashData(two));
        using var zip = Zip(new()
        {
            ["manifest.json"] = Bytes("""{"format":"GZCTF-GAME","version":"1.0"}"""),
            ["game.json"] = Bytes("""{"title":"Fixture"}"""),
            ["challenges/1.json"] = Bytes(System.Text.Json.JsonSerializer.Serialize(new
            {
                id = 1,
                title = "Fixture attachments",
                type = "DynamicAttachment",
                flags = new
                {
                    @static = new[]
                {
                    new { value = "fixture-one", attachment = new { type = "Local", hash = h1, fileName = "one.txt" } },
                    new { value = "fixture-two", attachment = new { type = "Local", hash = h2, fileName = "two.txt" } }
                }
                }
            })),
            ["challenges/2.json"] = Bytes("""{"id":2,"title":"Fixture container","type":"DynamicContainer","flags":{"template":"fixture{[GUID]}"},"container":{"image":"fixture/image","networkMode":"Open"}}"""),
            [$"files/{h1}"] = one,
            [$"files/{h2}"] = two
        });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.Equal(2, package.Challenges[0].Attachments.Count);
        Assert.Equal(new[] { "fixture-one", "fixture-two" }, package.Challenges[0].Attachments.Select(a => a.Flag));
        Assert.Equal("gzctf", package.Challenges[1].Container!.FlagMode);
        Assert.Equal("template", package.Challenges[1].Flags.Single().Kind);
        Assert.Equal("fixture{[GUID]}", package.Challenges[1].Flags.Single().Value);
        Assert.DoesNotContain(package.Issues, issue => issue.Severity == "error");
    }

    [Fact]
    public async Task Unknown_formats_and_malformed_manifest_are_controlled_errors()
    {
        using var unknown = Zip(new() { ["irrelevant.json"] = Bytes("{}") });
        await Assert.ThrowsAsync<InvalidDataException>(() => new PlatformArchiveReader().ReadAsync(unknown));
        using var malformed = Zip(new() { ["manifest.json"] = Bytes("not json") });
        await Assert.ThrowsAsync<InvalidDataException>(() => new PlatformArchiveReader().ReadAsync(malformed));
    }

    [Fact]
    public async Task Conflicting_historical_scores_block_import_instead_of_choosing_one()
    {
        using var zip = Ctfd(new()
        {
            ["challenges"] = """[{"id":1,"name":"Fixture","type":"standard","value":100}]""",
            ["flags"] = """[{"id":1,"challenge_id":1,"type":"static","content":"fixture"}]""",
            ["users"] = """[{"id":1,"name":"Fixture user"}]""",
            ["submissions"] = """[{"id":1,"user_id":1,"challenge_id":1,"type":"correct","value":100}]""",
            ["solves"] = """[{"id":1,"user_id":1,"challenge_id":1,"value":50}]"""
        });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.Contains(package.Issues, issue => issue.Code == "conflicting_history_score" && issue.Severity == "error");
    }

    [Fact]
    public async Task Gzctf_game_poster_is_a_validated_referenced_asset()
    {
        var hash = new string('a', 64);
        using var zip = Zip(new()
        {
            ["manifest.json"] = Bytes("""{"format":"GZCTF-GAME","version":"1.0"}"""),
            ["game.json"] = Bytes(System.Text.Json.JsonSerializer.Serialize(new { title = "Fixture", posterHash = hash })),
            ["challenges/1.json"] = Bytes("""{"id":1,"title":"Fixture","type":"StaticAttachment","flags":{"static":[{"value":"fixture"}]}}"""),
            [$"files/{hash}"] = Bytes("incorrect asset")
        });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.Contains(package.Issues, issue => issue.Code == "attachment_hash_mismatch" && issue.SourceId == "game");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Gzctf_preserves_all_body_aliases_and_explicit_download_role(bool explicitAttachment)
    {
        var asset = Bytes("fixture aliased asset");
        var hash = Convert.ToHexStringLower(SHA256.HashData(asset));
        var first = "/Assets/" + hash + "/fixture.png";
        var second = "https://fixture.example.invalid/api/Assets/" + hash + "/fixture.png?view=inline";
        using var zip = Zip(new()
        {
            ["manifest.json"] = Bytes("""{"format":"GZCTF-GAME","version":"1.0"}"""),
            ["game.json"] = Bytes("""{"title":"Fixture"}"""),
            ["challenges/1.json"] = Bytes(System.Text.Json.JsonSerializer.Serialize(new
            {
                id = 1,
                title = "Fixture",
                type = "StaticAttachment",
                content = $"![one]({first}) ![two]({second}) ![again]({first})",
                attachment = explicitAttachment ? new { type = "Local", hash, fileName = "download.txt" } : null,
                flags = new { @static = new[] { new { value = "fixture" } } }
            })),
            [$"files/{hash}"] = asset
        });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        var attachment = Assert.Single(package.Challenges.Single().Attachments);
        Assert.Equal(first, attachment.OriginalUrl);
        var metadata = System.Text.Json.JsonSerializer.SerializeToElement(attachment);
        Assert.True(metadata.TryGetProperty("OriginalUrls", out var aliases));
        Assert.Equal(new[] { first, second }, aliases.EnumerateArray().Select(alias => alias.GetString()));
        Assert.True(metadata.TryGetProperty("IsBodyResource", out var bodyResource));
        Assert.Equal(!explicitAttachment, bodyResource.GetBoolean());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ctfd_preserves_all_body_aliases_and_explicit_download_role(bool explicitAttachment)
    {
        const string first = "/files/folder/asset.txt";
        const string second = "https://fixture.example.invalid/files/folder/asset.txt?view=inline";
        using var zip = Ctfd(new()
        {
            ["challenges"] = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                new { id = 1, name = "Fixture", type = "standard", value = 100,
                    description = $"![one]({first}) ![two]({second}) ![again]({first})" }
            }),
            ["flags"] = """[{"id":1,"challenge_id":1,"type":"static","content":"fixture"}]""",
            ["files"] = explicitAttachment ? """[{"id":1,"challenge_id":1,"location":"folder/asset.txt"}]""" : "[]"
        }, new() { ["uploads/folder/asset.txt"] = Bytes("fixture asset") });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        var attachment = Assert.Single(package.Challenges.Single().Attachments);
        Assert.Equal(first, attachment.OriginalUrl);
        var metadata = System.Text.Json.JsonSerializer.SerializeToElement(attachment);
        Assert.True(metadata.TryGetProperty("OriginalUrls", out var aliases));
        Assert.Equal(new[] { first, second }, aliases.EnumerateArray().Select(alias => alias.GetString()));
        Assert.True(metadata.TryGetProperty("IsBodyResource", out var bodyResource));
        Assert.Equal(!explicitAttachment, bodyResource.GetBoolean());
    }

    [Fact]
    public async Task Remote_gzctf_attachments_are_preserved_as_links_without_fetching()
    {
        const string remote = "https://fixture.example.invalid/archive.bin";
        using var zip = Zip(new()
        {
            ["manifest.json"] = Bytes("""{"format":"GZCTF-GAME","version":"1.0"}"""),
            ["game.json"] = Bytes("""{"title":"Fixture"}"""),
            ["challenges/1.json"] = Bytes(System.Text.Json.JsonSerializer.Serialize(new
            {
                id = 1,
                title = "Fixture",
                type = "StaticAttachment",
                attachment = new { type = "Remote", remoteUrl = remote, fileName = "archive.bin" },
                flags = new { @static = new[] { new { value = "fixture" } } }
            }))
        });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.DoesNotContain(package.Issues, issue => issue.Severity == "error");
        Assert.Contains(package.Issues, issue => issue.Code == "remote_attachment_link_preserved" && issue.Severity == "warning");
        var attachment = Assert.Single(package.Challenges.Single().Attachments);
        Assert.Empty(attachment.ArchivePath);
        Assert.Empty(attachment.Sha256);
        var metadata = System.Text.Json.JsonSerializer.SerializeToElement(attachment);
        Assert.True(metadata.TryGetProperty("RemoteUrl", out var link));
        Assert.Equal(remote, link.GetString());
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:private-password@example.invalid/file")]
    public async Task Unsafe_remote_attachment_urls_are_blocked_without_exposing_url(string remote)
    {
        using var zip = Zip(new()
        {
            ["manifest.json"] = Bytes("""{"format":"GZCTF-GAME","version":"1.0"}"""),
            ["game.json"] = Bytes("""{"title":"Fixture"}"""),
            ["challenges/1.json"] = Bytes(System.Text.Json.JsonSerializer.Serialize(new
            {
                id = 1,
                title = "Fixture",
                type = "StaticAttachment",
                attachment = new { type = "Remote", remoteUrl = remote, fileName = "archive.bin" },
                flags = new { @static = new[] { new { value = "fixture" } } }
            }))
        });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.Contains(package.Issues, issue => issue.Code == "invalid_remote_attachment_url" && issue.Severity == "error");
        Assert.All(package.Issues, issue => Assert.DoesNotContain(remote, issue.Message));
    }

    [Fact]
    public async Task Parser_metadata_excludes_credentials_auth_tables_and_secret_config_recursively()
    {
        using var zip = Ctfd(new()
        {
            ["challenges"] = """[{"id":1,"name":"Fixture","type":"standard","value":100,"plugin":{"safe":"preserve-this-plugin-field","api_token":"private-plugin-token","endpoint":"https://user:private-embedded-password@example.invalid","data":"{\"nonce\":\"private-string-nonce\",\"safe\":\"safe-nested-plugin-field\"}"}}]""",
            ["flags"] = """[{"id":1,"challenge_id":1,"type":"static","content":"fixture-flag"}]""",
            ["users"] = """[{"id":1,"name":"Fixture user","password":"private-password-hash","secret":"private-user-secret","plugin":{"session":"private-session"}}]""",
            ["config"] = """[{"key":"ctf_name","value":"Fixture CTF"},{"key":"api_key","value":"private-config-key"}]""",
            ["submissions"] = """[{"id":1,"challenge_id":1,"user_id":1,"type":"incorrect","password":"private-submission-password"}]""",
            ["tokens"] = """[{"id":1,"value":"private-live-token"}]""",
            ["sessions"] = """[{"id":1,"value":"private-live-session"}]"""
        });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.Equal("private-password-hash", package.Users.Single().PasswordHash);
        Assert.Contains("preserve-this-plugin-field", package.MetadataJson);
        Assert.Contains("safe-nested-plugin-field", package.MetadataJson);
        Assert.Contains("preserve-this-plugin-field", package.Challenges.Single().MetadataJson);
        foreach (var secret in new[] { "private-plugin-token", "private-password-hash", "private-user-secret", "private-session", "private-config-key", "private-live-token", "private-live-session", "private-submission-password", "private-embedded-password", "private-string-nonce" })
        {
            Assert.DoesNotContain(secret, package.MetadataJson);
            Assert.DoesNotContain(secret, package.Users.Single().MetadataJson);
            Assert.DoesNotContain(secret, package.Challenges.Single().MetadataJson);
            Assert.DoesNotContain(secret, package.Submissions.Single().MetadataJson);
        }
    }

    [Theory]
    [InlineData("fixture{[UNKNOWN]}", true)]
    [InlineData("fixture{[GUID]}", false)]
    [InlineData("fixture{[TEAM_HASH]}", false)]
    [InlineData("[LEET]fixture{[GUID]}", false)]
    [InlineData("[CLEET]fixture{[TEAM_HASH]}", false)]
    public async Task Gzctf_unknown_template_markers_warn_without_changing_source_semantics(string template, bool warning)
    {
        using var zip = Zip(new()
        {
            ["manifest.json"] = Bytes("""{"format":"GZCTF-GAME","version":"1.0"}"""),
            ["game.json"] = Bytes("""{"title":"Fixture"}"""),
            ["challenges/1.json"] = Bytes(System.Text.Json.JsonSerializer.Serialize(new
            {
                id = 1,
                title = "Fixture",
                type = "DynamicContainer",
                flags = new { template },
                container = new { image = "fixture/image" }
            }))
        });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.Equal(template, package.Challenges.Single().Flags.Single().Value);
        Assert.DoesNotContain(package.Issues, issue => issue.Severity == "error");
        Assert.Equal(warning, package.Issues.Any(issue => issue.Code == "unrecognized_template_marker" && issue.Severity == "warning"));
    }

    [Theory]
    [InlineData("GZCTF-GAME", true)]
    [InlineData("GZCTF-GAME", false)]
    [InlineData("CTFd", true)]
    [InlineData("CTFd", false)]
    public async Task Missing_absolute_body_assets_remain_external_dependencies_and_relative_assets_block(string format, bool absolute)
    {
        var path = format == "CTFd" ? "/files/folder/missing.bin" : "/assets/" + new string('a', 64);
        var original = (absolute ? "HTTPS://fixture.example.invalid/base" : "") + path;
        var body = $"![fixture]({original})";
        using var zip = format == "CTFd"
            ? Ctfd(new()
            {
                ["challenges"] = System.Text.Json.JsonSerializer.Serialize(new[] { new { id = 1, name = "Fixture", type = "standard", value = 100, description = body } }),
                ["flags"] = """[{"id":1,"challenge_id":1,"type":"static","content":"fixture"}]"""
            })
            : Zip(new()
            {
                ["manifest.json"] = Bytes("""{"format":"GZCTF-GAME","version":"1.0"}"""),
                ["game.json"] = Bytes("""{"title":"Fixture"}"""),
                ["challenges/1.json"] = Bytes(System.Text.Json.JsonSerializer.Serialize(new
                {
                    id = 1,
                    title = "Fixture",
                    type = "StaticAttachment",
                    content = body,
                    flags = new { @static = new[] { new { value = "fixture" } } }
                }))
            });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.Equal(body, package.Challenges.Single().Body);
        Assert.Equal(!absolute, package.Issues.Any(issue => issue.Severity == "error"));
        Assert.Equal(absolute, package.Issues.Any(issue => issue.Code == "external_body_dependency" && issue.Severity == "warning"));
    }

    [Fact]
    public async Task Unmapped_nonempty_ctfd_tables_warn_without_retaining_or_disclosing_their_values()
    {
        using var zip = Ctfd(new()
        {
            ["challenges"] = """[{"id":1,"name":"Fixture","type":"standard","value":100}]""",
            ["flags"] = """[{"id":1,"challenge_id":1,"type":"static","content":"fixture"}]""",
            ["fields"] = """[{"id":1,"name":"Custom institution field"}]""",
            ["field_entries"] = """[{"id":1,"field_id":1,"user_id":1,"value":"private-custom-account-value"}]""",
            ["custom_plugin_state"] = """[{"id":1,"value":"private-plugin-state"}]""",
            ["empty_custom_table"] = "[]",
            ["tokens"] = """[{"id":1,"value":"private-live-token"}]""",
            ["sessions"] = """[{"id":1,"value":"private-live-session"}]""",
            ["tracking"] = """[{"id":1,"value":"private-tracking-value"}]""",
            ["alembic_version"] = """[{"version_num":"fixture-migration"}]"""
        });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        var warnings = package.Issues.Where(issue => issue.Code == "unmapped_backup_table").ToArray();
        Assert.Equal(new[] { "custom_plugin_state", "field_entries", "fields" }, warnings.Select(issue => issue.SourceId).OrderBy(id => id));
        Assert.All(warnings, issue => Assert.Equal("warning", issue.Severity));
        using var metadata = System.Text.Json.JsonDocument.Parse(package.MetadataJson);
        Assert.False(metadata.RootElement.TryGetProperty("fields", out _));
        Assert.False(metadata.RootElement.TryGetProperty("field_entries", out _));
        Assert.False(metadata.RootElement.TryGetProperty("custom_plugin_state", out _));
        foreach (var secret in new[] { "private-custom-account-value", "private-plugin-state", "private-live-token", "private-live-session", "private-tracking-value" })
        {
            Assert.DoesNotContain(secret, package.MetadataJson);
            Assert.All(package.Issues, issue => Assert.DoesNotContain(secret, issue.Message));
        }
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static MemoryStream Ctfd(Dictionary<string, string> tables, Dictionary<string, byte[]>? files = null)
    {
        var entries = files ?? new();
        foreach (var (name, rows) in tables)
            entries[$"db/{name}.json"] = Bytes("{\"results\":" + rows + "}");
        return Zip(entries);
    }

    private static MemoryStream Zip(Dictionary<string, byte[]> entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var (path, data) in entries)
            {
                using var output = archive.CreateEntry(path).Open();
                output.Write(data);
            }
        stream.Position = 0;
        return stream;
    }
}
