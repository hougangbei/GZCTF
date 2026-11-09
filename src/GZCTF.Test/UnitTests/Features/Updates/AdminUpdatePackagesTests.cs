using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Features.Updates.Api;
using GZCTF.Features.Updates.Application;
using GZCTF.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GZCTF.Test.UnitTests.Features.Updates;

public sealed class AdminUpdatePackagesTests
{
    private static AdminUpdatePackagesController Create() => new(
        new UpdateAgentClient(new ConfigurationBuilder().Build()),
        NullLogger<AdminUpdatePackagesController>.Instance)
    { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(1073741825L)]
    public async Task Upload_rejects_missing_empty_or_oversized_length(long? size)
    {
        var controller = Create();
        controller.Request.ContentLength = size;
        controller.Request.Headers["X-File-Name"] = "release.tar.gz";
        var result = Assert.IsAssignableFrom<ObjectResult>(await controller.Upload(CancellationToken.None));
        Assert.Equal(size > 1073741824 ? 413 : 400, result.StatusCode);
    }

    [Theory]
    [InlineData("..%2Fsecret.tar.gz")]
    [InlineData("C%3A%5Cfake.zip")]
    [InlineData("release.exe")]
    [InlineData("bad%0Aname.zip")]
    public async Task Upload_rejects_paths_and_unsupported_names_before_reading_body(string name)
    {
        var controller = Create();
        controller.Request.ContentLength = 10;
        controller.Request.Headers["X-File-Name"] = name;
        controller.Request.Body = new MemoryStream(new byte[10]);
        Assert.IsType<BadRequestObjectResult>(await controller.Upload(CancellationToken.None));
        Assert.Equal(0, controller.Request.Body.Position);
    }

    [Fact]
    public async Task Upload_without_service_is_friendly_and_does_not_consume_body()
    {
        var controller = Create();
        controller.Request.ContentLength = 10;
        controller.Request.Headers["X-File-Name"] = "release.tar.gz";
        controller.Request.Body = new MemoryStream(new byte[10]);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await controller.Upload(CancellationToken.None)).StatusCode);
        Assert.Equal(0, controller.Request.Body.Position);
    }

    [Fact]
    public async Task Package_operations_reject_arbitrary_paths_and_unconfirmed_apply()
    {
        var controller = Create();
        Assert.IsType<BadRequestObjectResult>(await controller.Validate("../../status", CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Checksum(new string('a', 32), new("not-a-hash"), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Apply(new string('a', 32),
            new(new string('b', 32), Guid.NewGuid().ToString(), false), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Apply(new string('a', 32),
            new("bad", Guid.NewGuid().ToString(), true), CancellationToken.None));
    }

    [Fact]
    public void All_package_routes_require_administrator_and_disable_cache()
    {
        var type = typeof(AdminUpdatePackagesController);
        Assert.NotEmpty(type.GetCustomAttributes(typeof(RequireAdminAttribute), true));
        var cache = Assert.Single(type.GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>());
        Assert.True(cache.NoStore);
    }
}
