using System;
using GZCTF.Features.Imports.Application;
using Xunit;
namespace GZCTF.Test.UnitTests.Features.Imports;

public class ImportedFlagTests
{
    [Theory]
    [InlineData("gzctf-random")]
    [InlineData("ctfd-whale")]
    public void Random_gzctf_flags_are_valid_unique_per_start(string mode)
    {
        var user = Guid.NewGuid();
        var first = ImportedFlagGenerator.Generate(mode, null, "source", "12", user);
        Assert.StartsWith("flag{", first);
        Assert.EndsWith("}", first);
        Assert.NotEqual(first, ImportedFlagGenerator.Generate(mode, null, "source", "12", user));
    }
    [Theory]
    [InlineData("native")]
    [InlineData("gzctf")]
    [InlineData("gzctf-random")]
    [InlineData("ctfd-whale")]
    public void Template_team_hash_uses_real_source_identity(string mode)
    {
        var user = Guid.NewGuid();
        var first = ImportedFlagGenerator.Generate(mode, "flag{[TEAM_HASH]}", "source", "12", user);
        Assert.DoesNotContain("TEAM_HASH", first);
        Assert.DoesNotContain("TestTeamHash", first);
        Assert.Equal(first, ImportedFlagGenerator.Generate(mode, "flag{[TEAM_HASH]}", "source", "12", user));
        Assert.NotEqual(first, ImportedFlagGenerator.Generate(mode, "flag{[TEAM_HASH]}", "other", "12", user));
    }
    [Theory]
    [InlineData("gzctf")]
    [InlineData("gzctf-random")]
    [InlineData("ctfd-whale")]
    public void Every_dynamic_mode_expands_the_same_custom_guid_template(string mode)
    {
        var flag = ImportedFlagGenerator.Generate(mode, "nynuctf{[GUID]}", "source", "12", Guid.NewGuid());
        Assert.StartsWith("nynuctf{", flag);
        Assert.True(Guid.TryParse(flag[8..^1], out _), flag);
    }

    [Theory]
    [InlineData("gzctf")]
    [InlineData("gzctf-random")]
    [InlineData("ctfd-whale")]
    public void Obsolete_user_id_templates_are_rejected(string mode)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ImportedFlagGenerator.Generate(mode, "flag{{userId}}", "source", "12", Guid.NewGuid()));
        Assert.Contains("[GUID]", error.Message);
    }
}
