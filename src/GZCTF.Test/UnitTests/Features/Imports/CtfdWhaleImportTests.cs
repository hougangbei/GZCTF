using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GZCTF.Features.Imports.Infrastructure;
using GZCTF.Utils;
using Xunit;

namespace GZCTF.Test.UnitTests.Features.Imports;

public sealed class CtfdWhaleImportTests
{
    [Theory]
    [InlineData("128m", "0.5", 128, 5)]
    [InlineData("1g", "1.2", 1024, 12)]
    public async Task Whale_imports_runtime_resources_without_requiring_static_flags(string memory, string cpu, int mb, int units)
    {
        using var zip = Archive(new()
        {
            ["challenges"] = """{"results":[{"id":1,"name":"Fixture","type":"dynamic_docker","value":123,"state":"visible"}]}""",
            ["dynamic_docker_challenge"] = $$"""{"results":[{"id":1,"docker_image":"fixture/image:latest","redirect_port":8080,"redirect_type":"http","memory_limit":"{{memory}}","cpu_limit":"{{cpu}}","initial":500,"minimum":100,"decay":20,"dynamic_score":1}]}""",
            ["teams"] = "", ["notifications"] = "", ["whale_container"] = ""
        });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.DoesNotContain(package.Issues, issue => issue.Severity == "error");
        var challenge = Assert.Single(package.Challenges);
        Assert.Equal(ChallengeType.DynamicContainer, challenge.Type);
        Assert.True(challenge.IsEnabled);
        Assert.Empty(challenge.Flags);
        Assert.Equal("fixture/image:latest", challenge.Container!.Image);
        Assert.Equal(8080, challenge.Container.ExposedPort);
        Assert.Equal(units, challenge.Container.Cpu);
        Assert.Equal(mb, challenge.Container.MemoryMb);
        Assert.Equal("ctfd-whale", challenge.Container.FlagMode);
        Assert.Contains("dynamic_docker_challenge", package.MetadataJson);
        Assert.Contains(package.Issues, issue => issue.Code == "dynamic_scoring_becomes_training_score");
        Assert.Contains(package.Issues, issue => issue.Code == "whale_runtime_recreated");
    }

    [Fact]
    public async Task Flagless_ordinary_content_is_a_disabled_draft_and_nonempty_malformed_tables_block()
    {
        using var zip = Archive(new()
        {
            ["challenges"] = """{"results":[{"id":1,"name":"Fixture","type":"dynamic","value":100,"state":"visible"}]}""",
            ["teams"] = "not json"
        });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.False(Assert.Single(package.Challenges).IsEnabled);
        Assert.Contains(package.Issues, issue => issue.Code == "missing_flags_disabled" && issue.Severity == "warning");
        Assert.Contains(package.Issues, issue => issue.Code == "malformed_table" && issue.Severity == "error");
    }

    [Fact]
    public async Task Whale_static_flags_take_precedence_over_instance_flags_as_in_source_plugin()
    {
        using var zip = Archive(new()
        {
            ["challenges"] = """{"results":[{"id":1,"name":"Fixture","type":"dynamic_docker","value":100}]}""",
            ["flags"] = """{"results":[{"id":1,"challenge_id":1,"type":"static","content":"fixture-static"},{"id":2,"challenge_id":1,"type":"static","content":"fixture-alternative"}]}""",
            ["dynamic_docker_challenge"] = """{"results":[{"id":1,"docker_image":"fixture/image","redirect_port":80,"redirect_type":"direct","memory_limit":"128m","cpu_limit":0.5}]}"""
        });
        var challenge = Assert.Single((await new PlatformArchiveReader().ReadAsync(zip)).Challenges);
        Assert.Equal(ChallengeType.StaticContainer, challenge.Type);
        Assert.Equal(new[] { "fixture-static", "fixture-alternative" }, challenge.Flags.Select(flag => flag.Value));
        Assert.Equal("ctfd-whale", challenge.Container!.FlagMode);
    }

    [Theory]
    [InlineData("79228162514264337593543950335t")]
    [InlineData("invalid")]
    public async Task Invalid_Whale_memory_is_a_controlled_preview_error(string memory)
    {
        using var zip = Archive(new()
        {
            ["challenges"] = """{"results":[{"id":1,"name":"Fixture","type":"dynamic_docker","value":100}]}""",
            ["dynamic_docker_challenge"] = $$"""{"results":[{"id":1,"docker_image":"fixture/image","redirect_port":80,"redirect_type":"direct","memory_limit":"{{memory}}","cpu_limit":0.5}]}"""
        });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.Contains(package.Issues, issue => issue.Code == "invalid_whale_runtime" && issue.Severity == "error");
    }

    [Fact]
    public async Task Whale_live_state_and_control_configuration_are_not_preserved()
    {
        using var zip = Archive(new()
        {
            ["challenges"] = """{"results":[{"id":1,"name":"Fixture","type":"standard","value":100}]}""",
            ["flags"] = """{"results":[{"id":1,"challenge_id":1,"type":"static","content":"fixture"}]}""",
            ["whale_container"] = """{"results":[{"id":1,"flag":"private-live-flag","uuid":"private-live-container"}]}""",
            ["whale_config"] = """{"results":[{"key":"docker_api_url","value":"private-control-endpoint"}]}"""
        });
        var package = await new PlatformArchiveReader().ReadAsync(zip);
        Assert.Contains(package.Issues, issue => issue.Code == "whale_infrastructure_ignored");
        Assert.DoesNotContain("private-live", package.MetadataJson);
        Assert.DoesNotContain("private-control", package.MetadataJson);
    }

    private static MemoryStream Archive(Dictionary<string, string> tables)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var (table, contents) in tables)
            {
                using var entry = zip.CreateEntry($"db/{table}.json").Open();
                entry.Write(Encoding.UTF8.GetBytes(contents));
            }
        stream.Position = 0;
        return stream;
    }
}
