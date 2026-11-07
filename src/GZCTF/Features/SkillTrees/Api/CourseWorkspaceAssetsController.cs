using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Features.SkillTrees.Application;
using GZCTF.Middlewares;
using GZCTF.Storage.Interface;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.SkillTrees.Api;

public sealed record CourseAssetResponse(string Hash, string Name, string StorageKey, string Url);

[RequireAdmin, ApiController, Route("api/admin/skill-trees/{id:guid}/workspace/assets")]
public sealed class CourseWorkspaceAssetsController(AppDbContext db, IBlobStorage storage, CourseAssetRegistry registry) : ControllerBase
{
    private const long MaxFileBytes = 256L * 1024 * 1024;
    [HttpPost, AuditAction("course_workspace.assets.upload"), RequestSizeLimit(MaxFileBytes + 65536)]
    public async Task<IActionResult> Upload(Guid id, [FromForm] List<IFormFile> files, CancellationToken token)
    {
        if (!await db.CourseAuthoringWorkspaces.AnyAsync(w => w.SkillTreeId == id && w.SkillTree.DeletedAtUtc == null, token)) return NotFound();
        if (files.Count == 0 || files.Sum(f => f.Length) > MaxFileBytes || files.Any(f => f.Length <= 0)) return BadRequest();
        var result = new List<CourseAssetResponse>();
        foreach (var file in files)
        {
            var name = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 255) return BadRequest();
            string hash;
            await using (var input = file.OpenReadStream()) hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, token));
            var key = AssetKey(id, hash);
            await using (var input = file.OpenReadStream()) await registry.StoreAsync(key, hash, input, token);
            result.Add(new(hash, name, key, $"/api/admin/skill-trees/{id}/workspace/assets/{hash}/{Uri.EscapeDataString(name)}"));
        }
        return Ok(result);
    }
    [HttpGet("{hash:length(64)}/{filename}"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(Guid id, string hash, string filename, CancellationToken token)
    {
        if (!ValidHash(hash) || !await db.CourseAuthoringWorkspaces.AnyAsync(w => w.SkillTreeId == id && w.SkillTree.DeletedAtUtc == null, token)) return NotFound();
        var key = AssetKey(id, hash);
        if (!await storage.ExistsAsync(key, token)) return NotFound();
        var type = new FileExtensionContentTypeProvider().TryGetContentType(filename, out var value) ? value : "application/octet-stream";
        return File(await storage.OpenReadAsync(key, token), type);
    }
    internal static string AssetKey(Guid treeId, string hash) => StoragePath.Combine("course-assets", treeId.ToString("N"), hash);
    internal static bool ValidHash(string hash) => hash.Length == 64 && hash.All(char.IsAsciiHexDigitLower);
}

[ApiController, Route("api/course-assets/{treeId:guid}/{version:guid}")]
public sealed class CoursePublishedAssetsController(AppDbContext db, IBlobStorage storage) : ControllerBase
{
    [HttpGet("{hash:length(64)}/{filename}")]
    public async Task<IActionResult> Get(Guid treeId, Guid version, string hash, string filename, CancellationToken token)
    {
        if (!CourseWorkspaceAssetsController.ValidHash(hash)) return NotFound();
        var prefix = $"/api/course-assets/{treeId}/{version}/{hash}/";
        var payloads = await db.CoursePublishedContents.AsNoTracking().Where(c => c.VersionId == version && c.Version.SkillTreeId == treeId &&
            c.Version.SkillTree.DeletedAtUtc == null && c.Version.SkillTree.CurrentPublishedRevisionId == version)
            .Select(c => c.PayloadJson).ToArrayAsync(token);
        if (!payloads.Any(p => ContainsBodyReference(p, prefix))) return NotFound();
        var key = CourseWorkspaceAssetsController.AssetKey(treeId, hash);
        if (!await storage.ExistsAsync(key, token)) return NotFound();
        var type = new FileExtensionContentTypeProvider().TryGetContentType(filename, out var value) ? value : "application/octet-stream";
        return File(await storage.OpenReadAsync(key, token), type);
    }
    internal static bool ContainsBodyReference(string payload, string prefix)
    {
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("localizations", out var localizations)) return false;
        foreach (var localization in localizations.EnumerateArray())
            foreach (var property in localization.EnumerateObject())
                if (property.Name.Equals("body", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String &&
                    property.Value.GetString()!.Contains(prefix, StringComparison.Ordinal)) return true;
        return false;
    }
}

[RequireAdmin, ApiController, Route("api/admin/content-assets")]
public sealed class ContentAssetsController(AppDbContext db, IBlobStorage storage, CourseAssetRegistry registry) : ControllerBase
{
    private const long MaxFileBytes = 256L * 1024 * 1024;
    [HttpPost, AuditAction("content_assets.upload"), RequestSizeLimit(MaxFileBytes + 65536)]
    public async Task<IActionResult> Upload([FromQuery] string kind, [FromQuery] Guid contentId, [FromForm] List<IFormFile> files, CancellationToken token)
    {
        if (!await ExistsAsync(kind, contentId, token)) return NotFound();
        if (files.Count == 0 || files.Sum(f => f.Length) > MaxFileBytes || files.Any(f => f.Length <= 0)) return BadRequest();
        var result = new List<CourseAssetResponse>();
        foreach (var file in files)
        {
            var name = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 255) return BadRequest();
            string hash;
            await using (var input = file.OpenReadStream()) hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, token));
            var key = AssetKey(kind, contentId, hash);
            await using (var input = file.OpenReadStream()) await registry.StoreAsync(key, hash, input, token);
            result.Add(new(hash, name, key, $"/api/admin/content-assets/{kind}/{contentId}/{hash}/{Uri.EscapeDataString(name)}"));
        }
        return Ok(result);
    }
    [HttpGet("{kind}/{contentId:guid}/{hash:length(64)}/{filename}"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(string kind, Guid contentId, string hash, string filename, CancellationToken token)
    {
        if (!CourseWorkspaceAssetsController.ValidHash(hash) || !await ExistsAsync(kind, contentId, token)) return NotFound();
        var key = AssetKey(kind, contentId, hash);
        if (!await storage.ExistsAsync(key, token)) return NotFound();
        var type = new FileExtensionContentTypeProvider().TryGetContentType(filename, out var value) ? value : "application/octet-stream";
        return File(await storage.OpenReadAsync(key, token), type);
    }
    private Task<bool> ExistsAsync(string kind, Guid id, CancellationToken token) => kind switch {
        "lesson" => db.Lessons.AnyAsync(l => l.Id == id, token),
        "challenge" => db.Challenges.AnyAsync(c => c.Id == id, token),
        _ => Task.FromResult(false)
    };
    internal static string AssetKey(string kind, Guid id, string hash) => StoragePath.Combine("content-assets", kind, id.ToString("N"), hash);
}

[ApiController, Route("api/content-assets/{kind}/{contentId:guid}")]
public sealed class PublishedContentAssetsController(AppDbContext db, IBlobStorage storage) : ControllerBase
{
    [HttpGet("{hash:length(64)}/{filename}")]
    public async Task<IActionResult> Get(string kind, Guid contentId, string hash, string filename, CancellationToken token)
    {
        if (kind is not ("lesson" or "challenge") || !CourseWorkspaceAssetsController.ValidHash(hash)) return NotFound();
        var prefix = $"/api/content-assets/{kind}/{contentId}/{hash}/";
        var payloads = await GZCTF.Features.ChallengeRuntime.Application.PublishedCourseContentReader.ActiveContents(db)
            .Where(c => c.Kind == kind && c.ContentId == contentId).Select(c => c.PayloadJson).ToArrayAsync(token);
        if (!payloads.Any(p => CoursePublishedAssetsController.ContainsBodyReference(p, prefix))) return NotFound();
        var key = ContentAssetsController.AssetKey(kind, contentId, hash);
        if (!await storage.ExistsAsync(key, token)) return NotFound();
        var type = new FileExtensionContentTypeProvider().TryGetContentType(filename, out var value) ? value : "application/octet-stream";
        return File(await storage.OpenReadAsync(key, token), type);
    }
}
