using System;
using System.Linq;
using GZCTF.Features.Imports.Api;
using GZCTF.Features.Imports.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Metadata;
using Xunit;

namespace GZCTF.Test.UnitTests.Features.Imports;

public sealed class PlatformUploadLimitTests
{
    [Theory]
    [InlineData("Preview")]
    [InlineData("Execute")]
    public void Full_backup_upload_limits_cover_a_194MB_backup_and_multipart_overhead(string action)
    {
        Assert.Equal(256L * 1024 * 1024, PlatformArchiveReader.MaxCompressedBytes);
        var method = typeof(PlatformImportsController).GetMethod(action)!;
        var request = method.GetCustomAttributes(typeof(RequestSizeLimitAttribute), false)
            .Cast<RequestSizeLimitAttribute>().Single();
        var form = method.GetCustomAttributes(typeof(RequestFormLimitsAttribute), false)
            .Cast<RequestFormLimitsAttribute>().Single();
        Assert.True(((IRequestSizeLimitMetadata)request).MaxRequestBodySize > PlatformArchiveReader.MaxCompressedBytes);
        Assert.Equal(PlatformArchiveReader.MaxCompressedBytes, form.MultipartBodyLengthLimit);
        Assert.Equal(256L * 1024 * 1024, PlatformArchiveReader.MaxExpandedBytes);
    }
}
