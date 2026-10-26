using System;
using GZCTF;
using System.IO;
using GZCTF.Repositories;
using Xunit;

namespace GZCTF.Test.UnitTests.Storage;

public sealed class ImageStreamOwnershipTests
{
    [Fact]
    public void Decoding_preserves_the_callers_stream_for_header_detection_and_storage()
    {
        using var resource = typeof(Program).Assembly.GetManifestResourceStream("GZCTF.Resources.favicon.webp");
        Assert.NotNull(resource);
        using var stream = new MemoryStream();
        resource.CopyTo(stream);
        stream.Position = 0;
        using var bitmap = BlobRepository.DecodeImage(stream);
        Assert.NotNull(bitmap);
        Assert.True(stream.CanSeek);
        stream.Position = 0;
        Assert.Equal((int)'R', stream.ReadByte());
    }
}
