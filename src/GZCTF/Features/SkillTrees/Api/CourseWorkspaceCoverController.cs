using System.Text.Json;
using GZCTF.Features.Shared;
using GZCTF.Features.SkillTrees.Application;
using GZCTF.Middlewares;
using GZCTF.Storage.Interface;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace GZCTF.Features.SkillTrees.Api;

[RequireAdmin, ApiController, Route("api/admin/skill-trees/{id:guid}/workspace/cover")]
public sealed class CourseWorkspaceCoverController(AppDbContext db, IBlobStorage storage, CourseAssetRegistry registry) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    [HttpPost, AuditAction("course_workspace.cover.update"), RequestSizeLimit(SkillTreeCoverService.MaxBytes + 65536)]
    public async Task<IActionResult> Upload(Guid id, [FromForm] string version, [FromForm] IFormFile file, CancellationToken token)
    {
        var workspace = await db.CourseAuthoringWorkspaces.Include(w => w.SkillTree)
            .SingleOrDefaultAsync(w => w.SkillTreeId == id && w.SkillTree.DeletedAtUtc == null, token);
        if (workspace is null) return NotFound();
        if (!Guid.TryParse(version, out var expected) || workspace.Version != expected) return ConflictResult();
        if (file.Length is <= 0 or > SkillTreeCoverService.MaxBytes) return BadRequest();
        using var input = new MemoryStream(); await file.CopyToAsync(input, token);
        if (input.Length > SkillTreeCoverService.MaxBytes) return BadRequest();
        input.Position = 0; using var managed = new SKManagedStream(input, false); using var codec = SKCodec.Create(managed);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp) ||
            codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 32_000_000) return BadRequest();
        using var bitmap = new SKBitmap(codec.Info);
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) return BadRequest();
        var width = Math.Min(1280, bitmap.Width); var height = Math.Max(1, (int)Math.Round(width * 9d / 16));
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        if (surface is null) return BadRequest();
        var scale = Math.Max(width / (float)bitmap.Width, height / (float)bitmap.Height);
        var drawWidth = bitmap.Width * scale; var drawHeight = bitmap.Height * scale;
        using var source = SKImage.FromBitmap(bitmap);
        surface.Canvas.DrawImage(source, new SKRect((width - drawWidth) / 2, (height - drawHeight) / 2,
            (width + drawWidth) / 2, (height + drawHeight) / 2), new SKSamplingOptions(SKFilterMode.Linear));
        using var image = surface.Snapshot(); using var encoded = image.Encode(SKEncodedImageFormat.Webp, 85);
        if (encoded is null || encoded.Size > SkillTreeCoverService.MaxBytes) return BadRequest();
        var key = StoragePath.Combine("course-covers", id.ToString("N"), Guid.NewGuid().ToString("N"));
        using (var output = new MemoryStream(encoded.ToArray())) await registry.StoreAsync(key, key, output, token);
        var document = JsonSerializer.Deserialize<CourseDocument>(workspace.DocumentJson, JsonOptions)!;
        workspace.CoverStorageKey = key;
        workspace.DocumentJson = JsonSerializer.Serialize(document with { CoverUrl = $"/api/admin/skill-trees/{id}/workspace/cover/image" }, JsonOptions);
        workspace.Version = Guid.NewGuid();
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { await storage.DeleteAsync(key, CancellationToken.None); return ConflictResult(); }
        catch { await storage.DeleteAsync(key, CancellationToken.None); throw; }
        return Ok(Response(workspace));
    }
    [HttpDelete, AuditAction("course_workspace.cover.delete")]
    public async Task<IActionResult> Remove(Guid id, CourseVersionCommand command, CancellationToken token)
    {
        var workspace = await db.CourseAuthoringWorkspaces.Include(w => w.SkillTree)
            .SingleOrDefaultAsync(w => w.SkillTreeId == id && w.SkillTree.DeletedAtUtc == null, token);
        if (workspace is null) return NotFound();
        if (!Guid.TryParse(command.Version, out var expected) || workspace.Version != expected) return ConflictResult();
        var document = JsonSerializer.Deserialize<CourseDocument>(workspace.DocumentJson, JsonOptions)!;
        workspace.CoverStorageKey = null; workspace.DocumentJson = JsonSerializer.Serialize(document with { CoverUrl = null }, JsonOptions);
        workspace.Version = Guid.NewGuid();
        try { await db.SaveChangesAsync(token); } catch (DbUpdateConcurrencyException) { return ConflictResult(); }
        return Ok(Response(workspace));
    }
    [HttpGet("image"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Image(Guid id, CancellationToken token)
    {
        var key = await db.CourseAuthoringWorkspaces.AsNoTracking().Where(w => w.SkillTreeId == id && w.SkillTree.DeletedAtUtc == null)
            .Select(w => w.CoverStorageKey).SingleOrDefaultAsync(token);
        return key is null || !await storage.ExistsAsync(key, token) ? NotFound() : File(await storage.OpenReadAsync(key, token), "image/webp");
    }
    private IActionResult ConflictResult() => Conflict(ApiError.Conflict(SkillTreeProblemCodes.RevisionConflict, "The course workspace changed since it was loaded.", HttpContext.TraceIdentifier));
    private static CourseWorkspaceResponse Response(GZCTF.Features.SkillTrees.Domain.CourseAuthoringWorkspace w) =>
        new(w.SkillTreeId, w.Version.ToString(), JsonSerializer.Deserialize<CourseDocument>(w.DocumentJson, JsonOptions)!, w.SkillTree.CurrentPublishedRevisionId != null);
}

[ApiController, Route("api/skill-trees/{id:guid}/cover")]
public sealed class CoursePublishedCoverController(AppDbContext db, IBlobStorage storage) : ControllerBase
{
    [HttpGet("{version:guid}")]
    public async Task<IActionResult> Image(Guid id, Guid version, CancellationToken token)
    {
        var key = await db.CoursePublishedVersions.AsNoTracking().Where(v => v.Id == version && v.SkillTreeId == id &&
            v.SkillTree.DeletedAtUtc == null && v.SkillTree.CurrentPublishedRevisionId == version).Select(v => v.CoverStorageKey).SingleOrDefaultAsync(token);
        return key is null || !await storage.ExistsAsync(key, token) ? NotFound() : File(await storage.OpenReadAsync(key, token), "image/webp");
    }
}
