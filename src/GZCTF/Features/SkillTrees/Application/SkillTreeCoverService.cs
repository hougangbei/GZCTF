using System.Security.Cryptography;
using GZCTF.Storage.Interface;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace GZCTF.Features.SkillTrees.Application;

public sealed class SkillTreeCoverService(AppDbContext db, IBlobStorage storage, ISkillTreeCacheInvalidator cache)
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public async Task<SkillTreeCoverResponse?> SetAsync(Guid id, uint rowVersion, IFormFile? file, CancellationToken token)
    {
        var tree = await db.SkillTrees.FirstOrDefaultAsync(tree => tree.Id == id && tree.DeletedAtUtc == null, token);
        if (tree is null) return null;
        SkillService.RequireVersion(rowVersion, tree.RowVersion);
        string? hash = null;
        if (file is not null)
        {
            if (file.Length is <= 0 or > MaxBytes) throw new SkillCategoryValidationException("Choose a JPEG, PNG or WebP image up to 5 MB.");
            using var input = new MemoryStream();
            await file.CopyToAsync(input, token);
            if (input.Length > MaxBytes) throw new SkillCategoryValidationException("The image is too large.");
            input.Position = 0;
            using var managed = new SKManagedStream(input, false);
            using var codec = SKCodec.Create(managed);
            if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp) ||
                codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 32_000_000)
                throw new SkillCategoryValidationException("Choose a valid JPEG, PNG or WebP image.");
            using var bitmap = new SKBitmap(codec.Info);
            if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
                throw new SkillCategoryValidationException("The image could not be decoded completely.");
            var width = Math.Min(1280, bitmap.Width);
            var height = Math.Max(1, (int)Math.Round(width * 9d / 16));
            using var surface = SKSurface.Create(new SKImageInfo(width, height))
                ?? throw new SkillCategoryValidationException("The image could not be resized.");
            var scale = Math.Max(width / (float)bitmap.Width, height / (float)bitmap.Height);
            var drawWidth = bitmap.Width * scale; var drawHeight = bitmap.Height * scale;
            using var source = SKImage.FromBitmap(bitmap);
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.DrawImage(source,
                new SKRect((width - drawWidth) / 2, (height - drawHeight) / 2, (width + drawWidth) / 2, (height + drawHeight) / 2),
                new SKSamplingOptions(SKFilterMode.Linear));
            using var image = surface.Snapshot();
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, 85);
            if (encoded is null) throw new SkillCategoryValidationException("The image could not be encoded.");
            var bytes = encoded.ToArray();
            if (bytes.Length > MaxBytes) throw new SkillCategoryValidationException("The converted cover is too large.");
            hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var path = StoragePath.Combine(PathHelper.Uploads, hash[..2], hash[2..4], hash);
            if (!await storage.ExistsAsync(path, token))
            {
                using var output = new MemoryStream(bytes);
                await storage.WriteAsync(path, output, cancellationToken: token);
            }
        }
        tree.CoverHash = hash;
        tree.UpdatedAtUtc = DateTimeOffset.UtcNow;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new SkillTreeRevisionConflictException(); }
        await cache.InvalidateTreeAsync(id, token);
        // Covers are content addressed and may be shared. Detaching never deletes stored bytes.
        return new(id, SkillHierarchyReader.CoverUrl(hash), tree.RowVersion);
    }
}
