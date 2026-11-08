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
    [Fact]
    public void Template_team_hash_uses_real_source_identity()
    {
        var user = Guid.NewGuid();
        var first = ImportedFlagGenerator.Generate("gzctf", "flag{[TEAM_HASH]}", "source", "12", user);
        Assert.DoesNotContain("TEAM_HASH", first);
        Assert.DoesNotContain("TestTeamHash", first);
        Assert.Equal(first, ImportedFlagGenerator.Generate("gzctf", "flag{[TEAM_HASH]}", "source", "12", user));
        Assert.NotEqual(first, ImportedFlagGenerator.Generate("gzctf", "flag{[TEAM_HASH]}", "other", "12", user));
    }
}
