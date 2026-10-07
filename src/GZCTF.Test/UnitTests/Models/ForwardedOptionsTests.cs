using System.Collections.Generic;
using System.Net;
using GZCTF.Models.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GZCTF.Test.UnitTests.Models;

public class ForwardedOptionsTests
{
    [Fact]
    public void TrustedIpv4ProxyAlsoMatchesDualModeSocketAddress()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
        [
            new KeyValuePair<string, string?>("ForwardedOptions:ForwardedHeaders", "XForwardedFor, XForwardedProto"),
            new KeyValuePair<string, string?>("ForwardedOptions:KnownProxies:0", "192.0.2.10")
        ]).Build();
        var configured = configuration.GetSection("ForwardedOptions").Get<ForwardedOptions>();
        Assert.NotNull(configured);

        var options = new ForwardedHeadersOptions();
        configured.ToForwardedHeadersOptions(options);

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            options.ForwardedHeaders);
        Assert.Contains(IPAddress.Parse("192.0.2.10"), options.KnownProxies);
        Assert.Contains(IPAddress.Parse("::ffff:192.0.2.10"), options.KnownProxies);
    }
}
