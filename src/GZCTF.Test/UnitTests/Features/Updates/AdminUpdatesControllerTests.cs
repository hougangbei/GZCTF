using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using GZCTF.Services.Config;
using GZCTF.Models.Internal;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Features.Updates.Api;
using GZCTF.Features.Updates.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GZCTF.Test.UnitTests.Features.Updates;

public sealed class AdminUpdatesControllerTests
{
    private static AdminUpdatesController Create() => new(new UpdateAgentClient(new ConfigurationBuilder().Build()),
        NullLogger<AdminUpdatesController>.Instance, new ConfigurationBuilder().Build(), null!)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    [Fact]
    public async Task Every_successful_save_persists_a_new_server_generated_generation()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var store = new ConfigurationStore(configuration);
        var controller = new AdminUpdatesController(new UpdateAgentClient(configuration),
            NullLogger<AdminUpdatesController>.Instance, configuration, store)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var submitted = new UpdateSourceSettings { Generation = "client-controlled-invalid-value" };
        var first = Assert.IsType<UpdateSourceSettings>(Assert.IsType<OkObjectResult>(
            await controller.SaveSource(submitted, CancellationToken.None)).Value);
        Assert.Matches("^[0-9a-f]{32}$", first.Generation);
        Assert.Equal(first.Generation, Assert.IsType<UpdateSourceSettings>(
            Assert.IsType<OkObjectResult>(controller.Source()).Value).Generation);
        submitted.Generation = first.Generation;
        var second = Assert.IsType<UpdateSourceSettings>(Assert.IsType<OkObjectResult>(
            await controller.SaveSource(submitted, CancellationToken.None)).Value);
        Assert.NotEqual(first.Generation, second.Generation);
        Assert.Equal(second.Generation, Assert.IsType<UpdateSourceSettings>(
            Assert.IsType<OkObjectResult>(controller.Source()).Value).Generation);
    }

    [Fact]
    public void Source_is_available_without_host_agent()
    {
        var result = Assert.IsType<OkObjectResult>(Create().Source());
        var source = Assert.IsType<UpdateSourceSettings>(result.Value);
        Assert.Equal("https://github.com/hougangbei/GZCTF", source.Location);
    }

    [Fact]
    public async Task Invalid_source_does_not_reach_configuration_storage()
    {
        var result = await Create().SaveSource(new() { Kind = "directory", Location = "relative", Ref = "" }, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Validation_without_agent_returns_friendly_unavailable()
    {
        var result = Assert.IsType<ObjectResult>(await Create().Validate(CancellationToken.None));
        Assert.Equal(503, result.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../../../etc/passwd")]
    [InlineData("0123456789abcdef0123456789abcdef0")]
    public async Task Apply_rejects_malformed_validation_id(string? validationId)
    {
        var result = await Create().Apply(new(null, validationId), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Update_endpoints_disable_response_caching()
    {
        var attribute = Assert.Single(typeof(AdminUpdatesController).GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Cast<ResponseCacheAttribute>());
        Assert.True(attribute.NoStore);
        Assert.Equal(ResponseCacheLocation.None, attribute.Location);
    }
    private sealed class ConfigurationStore(IConfiguration configuration) : IConfigService
    {
        public Task SaveConfig<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(
            T config, CancellationToken token = default) where T : class =>
            SaveConfigSet(ConfigService.GetConfigs(config), token);
        public Task SaveConfigSet(HashSet<GZCTF.Models.Data.Config> configs, CancellationToken token = default)
        {
            foreach (var entry in configs) configuration[entry.ConfigKey] = entry.Value;
            return Task.CompletedTask;
        }
        public Task SaveConfig([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type,
            object? value, CancellationToken token = default) => throw new NotSupportedException();
        public Task UpdateApiEncryptionKey(CancellationToken token = default) => throw new NotSupportedException();
        public string? DecryptApiData(string cipherText) => throw new NotSupportedException();
        public Task<SignatureContext> GetApiTokenContext(CancellationToken token = default) => throw new NotSupportedException();
        public byte[] GetXorKey() => throw new NotSupportedException();
        public void ReloadConfig() { }
    }

}
