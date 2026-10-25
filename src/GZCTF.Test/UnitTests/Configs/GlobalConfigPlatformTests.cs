using GZCTF.Models.Internal;
using Xunit;

namespace GZCTF.Test.UnitTests.Configs;

public class GlobalConfigPlatformTests
{
    [Theory]
    [InlineData("NYNUSEC", "NYNUSEC")]
    [InlineData(" NYNUSEC ", "NYNUSEC")]
    [InlineData("", "GZCTF")]
    [InlineData("  ", "GZCTF")]
    public void Platform_UsesTheConfiguredNameWithoutAnAddedSuffix(string title, string expected)
    {
        var config = new GlobalConfig { Title = title };

        Assert.Equal(expected, config.Platform);
    }
}
