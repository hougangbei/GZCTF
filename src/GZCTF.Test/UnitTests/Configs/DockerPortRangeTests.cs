using System;
using GZCTF.Models.Internal;
using Xunit;

namespace GZCTF.Test.UnitTests.Configs;

public sealed class DockerPortRangeTests
{
    [Theory]
    [InlineData(null, "0")]
    [InlineData("", "0")]
    [InlineData("  ", "0")]
    [InlineData("30000-30199", "30000-30199")]
    [InlineData(" 30000-30199 ", "30000-30199")]
    [InlineData("65535-65535", "65535-65535")]
    public void Optional_range_preserves_default_or_returns_valid_Docker_binding(string? range, string binding)
        => Assert.Equal(binding, new DockerConfig { HostPortRange = range }.GetHostPortBinding());

    [Theory]
    [InlineData("0-30000")]
    [InlineData("30000-65536")]
    [InlineData("30199-30000")]
    [InlineData("30000")]
    [InlineData("30000-30199/tcp")]
    [InlineData("30000-30199,40000")]
    [InlineData("abc-def")]
    [InlineData("-1-30000")]
    public void Invalid_range_is_rejected_before_container_allocation(string range)
        => Assert.Throws<ArgumentException>(() => new DockerConfig { HostPortRange = range }.GetHostPortBinding());
}
