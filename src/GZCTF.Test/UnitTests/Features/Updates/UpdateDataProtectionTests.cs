using System;
using System.IO;
using GZCTF.Extensions.Startup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.Features.Updates;

public sealed class UpdateDataProtectionTests
{
    [Fact]
    public void Configured_application_name_preserves_protected_data_across_release_roots()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gzctf-dp-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var keys = Directory.CreateDirectory(Path.Combine(root, "keys"));
            using var first = Services(Path.Combine(root, "release-a"), "stable-gzctf", keys);
            using var second = Services(Path.Combine(root, "release-b"), "stable-gzctf", keys);
            var ciphertext = first.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("flag-attempt-details").Protect("existing details");
            Assert.Equal("existing details", second.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("flag-attempt-details").Unprotect(ciphertext));
            Assert.Equal("stable-gzctf", first.GetRequiredService<IOptions<DataProtectionOptions>>()
                .Value.ApplicationDiscriminator);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Unconfigured_application_name_preserves_framework_content_root_discriminator(string? applicationName)
    {
        var root = Path.Combine(Path.GetTempPath(), $"gzctf-dp-{Guid.NewGuid():N}");
        try
        {
            using var services = Services(root, applicationName, Directory.CreateDirectory(Path.Combine(root, "keys")));
            var actual = services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator;
            Assert.Equal(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, actual);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static ServiceProvider Services(string contentRoot, string? applicationName, DirectoryInfo keys)
    {
        Directory.CreateDirectory(contentRoot);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = contentRoot });
        builder.Configuration["DataProtection:ApplicationName"] = applicationName;
        builder.ConfigureIdentity();
        builder.Services.AddDataProtection().PersistKeysToFileSystem(keys);
        return builder.Services.BuildServiceProvider();
    }
}
