using System.Text.Json;
using GZCTF.Features.Shared;
using GZCTF.Features.SkillTrees.Application;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Middlewares;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.SkillTrees.Api;

public sealed record CourseDocument(string Name, string Summary, string IconKey, string? CoverUrl, List<CourseCategory> Categories);
public sealed record CourseCategory(Guid CategoryId, string Name, string Summary, string IconKey, List<CourseSkill> Skills);
public sealed record CourseSkill(Guid SkillId, string Name, string Summary, string IconKey, bool IsEnabled, List<CourseContent> Contents);
public sealed record CourseContent(Guid ContentId, string Kind, string Title, string Summary, string? State, int ExpectedMinutes = 0, string Difficulty = "");
public sealed record CourseWorkspaceResponse(Guid SkillTreeId, string Version, CourseDocument Document, bool HasPublishedVersion);
public sealed record SaveCourseWorkspaceCommand(string Version, CourseDocument Document);

[RequireAdmin, ApiController, Route("api/admin/skill-trees/{id:guid}/workspace")]
public sealed class CourseAuthoringController(AppDbContext db, CoursePublicationService publication) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet]
    public async Task<IActionResult> Get(Guid id, CancellationToken token)
    {
        var tree = await db.SkillTrees.AsNoTracking().Include(t => t.Revisions).ThenInclude(r => r.Categories).ThenInclude(r => r.Category)
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAtUtc == null, token);
        if (tree is null) return NotFound();
        var workspace = await db.CourseAuthoringWorkspaces.AsNoTracking().FirstOrDefaultAsync(w => w.SkillTreeId == id, token);
        if (workspace is null)
        {
            var revision = tree.Revisions.FirstOrDefault(r => r.Status == SkillTreeRevisionStatus.Draft)
                ?? tree.Revisions.FirstOrDefault(r => r.Id == tree.CurrentPublishedRevisionId);
            var refs = revision?.Categories.Where(r => r.Category.DeletedAtUtc == null).OrderBy(r => r.SortOrder).ToArray() ?? [];
            var skills = await SkillHierarchyReader.LoadSkillsAsync(db, refs.Select(r => r.CategoryId).ToArray(), false, token);
            var categories = refs.Select(r => new CourseCategory(r.CategoryId, r.Category.Name, r.Category.Summary, r.Category.IconKey,
                skills.GetValueOrDefault(r.CategoryId, []).Select(s => new CourseSkill(s.SkillId, s.Name, s.Summary, s.IconKey, s.IsEnabled,
                    s.Contents.Select(c => new CourseContent(c.ContentId, c.Kind, c.Title, c.Summary, c.State, c.ExpectedMinutes, c.Difficulty)).ToList())).ToList())).ToList();
            var document = new CourseDocument(tree.Name, tree.Summary, tree.IconKey, SkillHierarchyReader.CoverUrl(tree.CoverHash), categories);
            workspace = new CourseAuthoringWorkspace { SkillTreeId = id, DocumentJson = JsonSerializer.Serialize(document, JsonOptions) };
            db.CourseAuthoringWorkspaces.Add(workspace);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException)
            {
                db.Entry(workspace).State = EntityState.Detached;
                workspace = await db.CourseAuthoringWorkspaces.AsNoTracking().FirstAsync(w => w.SkillTreeId == id, token);
            }
        }
        return Ok(Response(workspace, tree.CurrentPublishedRevisionId != null));
    }

    [HttpPut, AuditAction("course_workspace.update")]
    public async Task<IActionResult> Put(Guid id, SaveCourseWorkspaceCommand command, CancellationToken token)
    {
        var workspace = await db.CourseAuthoringWorkspaces.Include(w => w.SkillTree)
            .FirstOrDefaultAsync(w => w.SkillTreeId == id && w.SkillTree.DeletedAtUtc == null, token);
        if (workspace is null) return NotFound();
        if (!Guid.TryParse(command.Version, out var version) || version != workspace.Version) return ConflictResult();
        var document = command.Document;
        if (document is null || string.IsNullOrWhiteSpace(document.Name) || document.Name.Length > 128 ||
            document.Summary is null || document.Summary.Length > 1024 || !SkillTreeIconCatalog.IsSupported(document.IconKey) || document.Categories is null)
            return BadRequest();
        if (document.Categories.Count > 100 || document.Categories.Any(c => c is null || c.CategoryId == Guid.Empty ||
            string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 128 || c.Summary is null || c.Summary.Length > 1024 ||
            !SkillTreeIconCatalog.IsSupported(c.IconKey) || c.Skills is null)) return BadRequest();
        var allSkills = document.Categories.SelectMany(c => c.Skills).ToArray();
        if (allSkills.Length > 1000 || allSkills.Any(s => s is null || s.SkillId == Guid.Empty ||
            string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 128 || s.Summary is null || s.Summary.Length > 1024 ||
            !SkillTreeIconCatalog.IsSupported(s.IconKey) || s.Contents is null)) return BadRequest();
        var allContents = allSkills.SelectMany(s => s.Contents).ToArray();
        if (allContents.Length > 5000 || allContents.Any(c => c is null || c.ContentId == Guid.Empty ||
            c.Title is null || c.Title.Length > 256 || c.Summary is null || c.Summary.Length > 2048 || c.State?.Length > 32)) return BadRequest();
        if (allSkills.GroupBy(s => s.SkillId).Any(g => g.Select(s => JsonSerializer.Serialize(s, JsonOptions)).Distinct().Count() != 1))
            return BadRequest(ApiError.Validation("course_inconsistent_skill", "The same skill has inconsistent definitions in this course.", HttpContext.TraceIdentifier, null));
        var categoryIds = document.Categories.Select(c => c.CategoryId).ToArray();
        var skillIds = document.Categories.SelectMany(c => c.Skills ?? []).Select(s => s.SkillId).Distinct().ToArray();
        var content = document.Categories.SelectMany(c => c.Skills ?? []).SelectMany(s => s.Contents ?? []).ToArray();
        if (categoryIds.Distinct().Count() != categoryIds.Length || document.Categories.Any(c => c.Skills is null || c.Skills.Select(s => s.SkillId).Distinct().Count() != c.Skills.Count) ||
            document.Categories.SelectMany(c => c.Skills).Any(s => s.Contents is null || s.Contents.Select(c => (c.Kind, c.ContentId)).Distinct().Count() != s.Contents.Count) ||
            content.Any(c => c.Kind is not ("lesson" or "challenge"))) return BadRequest();
        var lessonIds = content.Where(c => c.Kind == "lesson").Select(c => c.ContentId).Distinct().ToArray();
        var challengeIds = content.Where(c => c.Kind == "challenge").Select(c => c.ContentId).Distinct().ToArray();
        if (await db.SkillCategories.CountAsync(c => categoryIds.Contains(c.Id) && c.DeletedAtUtc == null, token) != categoryIds.Length ||
            await db.Skills.CountAsync(s => skillIds.Contains(s.Id) && s.DeletedAtUtc == null, token) != skillIds.Length ||
            await db.Lessons.CountAsync(l => lessonIds.Contains(l.Id), token) != lessonIds.Length ||
            await db.Set<GZCTF.Features.ChallengeLibrary.Domain.Challenge>().CountAsync(c => challengeIds.Contains(c.Id), token) != challengeIds.Length) return BadRequest();
        // Cover changes require a dedicated private upload path; arbitrary public URLs cannot be supplied.
        var previous = JsonSerializer.Deserialize<CourseDocument>(workspace.DocumentJson, JsonOptions)!;
        if (document.CoverUrl != previous.CoverUrl) return BadRequest();
        workspace.DocumentJson = JsonSerializer.Serialize(document, JsonOptions);
        workspace.Version = Guid.NewGuid();
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { return ConflictResult(); }
        return Ok(Response(workspace, workspace.SkillTree.CurrentPublishedRevisionId != null));
    }

    [HttpPost("check"), AuditAction("course_workspace.check")]
    public async Task<IActionResult> Check(Guid id, CourseVersionCommand command, CancellationToken token)
    {
        try { var result = await publication.CheckAsync(id, command.Version, token); return result is null ? NotFound() : Ok(result); }
        catch (SkillTreeRevisionConflictException) { return ConflictResult(); }
    }

    [HttpPost("publish"), AuditAction("course_workspace.publish")]
    public async Task<IActionResult> Publish(Guid id, CourseVersionCommand command, CancellationToken token)
    {
        try
        {
            var result = await publication.PublishAsync(id, command.Version, token);
            if (result is null) return NotFound();
            return result.Issues.Any(issue => issue.Severity == "error") ? BadRequest(result) : Ok(result);
        }
        catch (SkillTreeRevisionConflictException) { return ConflictResult(); }
        catch (Npgsql.PostgresException ex) when (ex.SqlState is "40001" or "40P01") { return ConflictResult(); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "40001" or "40P01" }) { return ConflictResult(); }
    }

    [HttpGet("references")]
    public async Task<IActionResult> References(Guid id, [FromQuery] string kind, [FromQuery] Guid resourceId, CancellationToken token)
    {
        if (kind is not ("category" or "skill" or "lesson" or "challenge")) return BadRequest();
        if (!await db.SkillTrees.AnyAsync(t => t.Id == id && t.DeletedAtUtc == null, token)) return NotFound();
        var versions = await (from tree in db.SkillTrees.AsNoTracking()
            join snapshot in db.CoursePublishedVersions.AsNoTracking() on tree.CurrentPublishedRevisionId equals snapshot.Id
            where tree.DeletedAtUtc == null select snapshot).ToListAsync(token);
        var result = versions.Select(v => (v.SkillTreeId, Document: JsonSerializer.Deserialize<CourseDocument>(v.PublicDocumentJson, JsonOptions)!))
            .Where(v => kind switch {
                "category" => v.Document.Categories.Any(c => c.CategoryId == resourceId),
                "skill" => v.Document.Categories.SelectMany(c => c.Skills).Any(s => s.SkillId == resourceId),
                _ => v.Document.Categories.SelectMany(c => c.Skills).SelectMany(s => s.Contents).Any(c => c.Kind == kind && c.ContentId == resourceId)
            }).Select(v => new CategoryTreeReferenceResponse(v.SkillTreeId, v.Document.Name, true)).ToArray();
        return Ok(result);
    }

    [HttpGet("preview")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken token)
    {
        try { var preview = await publication.PreviewAsync(id, token); return preview is null ? NotFound() : Ok(preview); }
        catch (SkillTreeRevisionConflictException) { return ConflictResult(); }
    }

    private IActionResult ConflictResult() => Conflict(ApiError.Conflict(SkillTreeProblemCodes.RevisionConflict,
        "The course workspace changed since it was loaded.", HttpContext.TraceIdentifier));
    private static CourseWorkspaceResponse Response(CourseAuthoringWorkspace workspace, bool published) =>
        new(workspace.SkillTreeId, workspace.Version.ToString(), JsonSerializer.Deserialize<CourseDocument>(workspace.DocumentJson, JsonOptions)!, published);
}
