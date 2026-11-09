using System.Text.Json;
using GZCTF.Features.SkillTrees.Application;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Middlewares;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.SkillTrees.Api;

public sealed record CourseResourceLocation(Guid TreeId, string TreeName, Guid CategoryId, string CategoryName,
    Guid? SkillId, string? SkillName, string State);
public sealed record CourseResourceRow(Guid Id, string Kind, string Name, string[] SourceLabels,
    CourseResourceLocation[] Locations, bool Unassociated, int ContentCount);

[RequireAdmin, ApiController, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), Route("api/admin/course-resources")]
public sealed class CourseResourcesController(AppDbContext db) : ControllerBase
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string kind, [FromQuery] string? search, [FromQuery] bool unassociated,
        [FromQuery] Guid? resourceId, CancellationToken token)
    {
        if (kind is not ("category" or "skill" or "challenge" or "lesson") || search?.Length > 256) return BadRequest();
        var locations = new Dictionary<Guid, List<CourseResourceLocation>>();
        var contentCounts = new Dictionary<Guid, HashSet<(string, Guid)>>();
        void Index(Guid treeId, CourseDocument doc, string state)
        {
            foreach (var category in doc.Categories)
            {
                void Add(Guid id, CourseResourceLocation location, IEnumerable<CourseContent> contents)
                {
                    if (!locations.TryGetValue(id, out var list)) locations[id] = list = [];
                    if (!list.Contains(location)) list.Add(location);
                    if (!contentCounts.TryGetValue(id, out var set)) contentCounts[id] = set = [];
                    foreach (var c in contents) set.Add((c.Kind, c.ContentId));
                }
                if (kind == "category") Add(category.CategoryId, new(treeId, doc.Name, category.CategoryId, category.Name, null, null, state), category.Skills.SelectMany(s => s.Contents));
                foreach (var skill in category.Skills)
                {
                    var location = new CourseResourceLocation(treeId, doc.Name, category.CategoryId, category.Name, skill.SkillId, skill.Name, state);
                    if (kind == "skill") Add(skill.SkillId, location, skill.Contents);
                    if (kind is "challenge" or "lesson") foreach (var c in skill.Contents.Where(c => c.Kind == kind)) Add(c.ContentId, location, [c]);
                }
            }
        }
        var trees = await db.SkillTrees.AsNoTracking().Where(t => t.DeletedAtUtc == null).Include(t => t.Revisions).ThenInclude(r => r.Categories).ThenInclude(c => c.Category).ToListAsync(token);
        var treeIds = trees.Select(t => t.Id).ToArray();
        var workspaces = await db.CourseAuthoringWorkspaces.AsNoTracking().Where(w => treeIds.Contains(w.SkillTreeId)).ToDictionaryAsync(w => w.SkillTreeId, token);
        var publishedIds = trees.Where(t => t.CurrentPublishedRevisionId != null).Select(t => t.CurrentPublishedRevisionId!.Value).ToArray();
        var published = await db.CoursePublishedVersions.AsNoTracking().Where(v => publishedIds.Contains(v.Id)).ToListAsync(token);
        foreach (var snapshot in published) Index(snapshot.SkillTreeId, JsonSerializer.Deserialize<CourseDocument>(snapshot.PublicDocumentJson, Options)!, "published");
        async Task<CourseDocument> LegacyDocument(SkillTree tree, SkillTreeRevision? revision)
        {
            var refs = revision?.Categories.Where(c => c.Category.DeletedAtUtc == null).OrderBy(c => c.SortOrder).ToArray() ?? [];
            var skills = await SkillHierarchyReader.LoadSkillsAsync(db, refs.Select(c => c.CategoryId).ToArray(), false, token);
            return new CourseDocument(tree.Name, tree.Summary, tree.IconKey, null, refs.Select(c => new CourseCategory(c.CategoryId, c.Category.Name, c.Category.Summary, c.Category.IconKey,
                skills.GetValueOrDefault(c.CategoryId, []).Select(s => new CourseSkill(s.SkillId, s.Name, s.Summary, s.IconKey, s.IsEnabled,
                    s.Contents.Select(x => new CourseContent(x.ContentId, x.Kind, x.Title, x.Summary, x.State)).ToList())).ToList())).ToList());
        }
        // Drafts and effective published revisions are independent even before snapshots were introduced.
        foreach (var tree in trees)
        {
            var current = tree.Revisions.FirstOrDefault(r => r.Id == tree.CurrentPublishedRevisionId);
            if (current is not null && !published.Any(p => p.Id == current.Id)) Index(tree.Id, await LegacyDocument(tree, current), "published");
            if (workspaces.TryGetValue(tree.Id, out var workspace)) { Index(tree.Id, JsonSerializer.Deserialize<CourseDocument>(workspace.DocumentJson, Options)!, "draft"); continue; }
            var draft = tree.Revisions.FirstOrDefault(r => r.Status == SkillTreeRevisionStatus.Draft) ?? current;
            Index(tree.Id, await LegacyDocument(tree, draft), "draft");
        }
        List<(Guid Id, string Name)> resources;
        switch (kind)
        {
            case "category": resources = (await db.SkillCategories.AsNoTracking().Where(c => c.DeletedAtUtc == null && c.MergedIntoCategoryId == null).Select(c => new { c.Id, c.Name }).ToListAsync(token)).Select(c => (c.Id, c.Name)).ToList(); break;
            case "skill": resources = (await db.Skills.AsNoTracking().Where(s => s.DeletedAtUtc == null).Select(s => new { s.Id, s.Name }).ToListAsync(token)).Select(s => (s.Id, s.Name)).ToList(); break;
            case "challenge": resources = (await db.Challenges.AsNoTracking().Select(c => new { c.Id, Name = c.Localizations.OrderBy(l => l.Locale).Select(l => l.Title).FirstOrDefault() }).ToListAsync(token)).Select(c => (c.Id, c.Name ?? c.Id.ToString())).ToList(); break;
            default: resources = (await db.Lessons.AsNoTracking().Select(l => new { l.Id, Name = l.Localizations.OrderBy(x => x.Locale).Select(x => x.Title).FirstOrDefault() }).ToListAsync(token)).Select(l => (l.Id, l.Name ?? l.Id.ToString())).ToList(); break;
        }
        var filtered = resources.Where(r => (!resourceId.HasValue || r.Id == resourceId) && (string.IsNullOrWhiteSpace(search) || r.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)) && (!unassociated || !locations.ContainsKey(r.Id))).ToArray();
        var ids = filtered.Select(r => r.Id).ToArray();
        // Library membership still explains a category's size when its last course reference is removed.
        if (kind is "category" or "skill")
        {
            var global = kind == "skill"
                ? await db.SkillContents.AsNoTracking().Where(c => ids.Contains(c.SkillId)).Select(c => new { Id = c.SkillId, c.ChallengeId, c.LessonId }).ToListAsync(token)
                : await (from r in db.CategorySkillRefs.AsNoTracking() join c in db.SkillContents.AsNoTracking() on r.SkillId equals c.SkillId
                    where ids.Contains(r.CategoryId) select new { Id = r.CategoryId, c.ChallengeId, c.LessonId }).ToListAsync(token);
            if (kind == "category") global.AddRange(await db.CategoryContents.AsNoTracking().Where(c => ids.Contains(c.CategoryId)).Select(c => new { Id = c.CategoryId, c.ChallengeId, c.LessonId }).ToListAsync(token));
            foreach (var c in global)
            {
                if (!contentCounts.TryGetValue(c.Id, out var set)) contentCounts[c.Id] = set = [];
                if (c.ChallengeId is { } challengeId) set.Add(("challenge", challengeId));
                if (c.LessonId is { } lessonId) set.Add(("lesson", lessonId));
            }
        }
        var maps = await db.PlatformImportMaps.AsNoTracking().Where(m => m.Kind == kind && ids.Contains(m.TargetId))
            .Select(m => new { m.TargetId, m.Batch.Title, m.Batch.SourceInstance, m.Batch.Format }).ToListAsync(token);
        var labels = maps.GroupBy(m => m.TargetId).ToDictionary(g => g.Key, g => g.Select(m => $"{Safe(m.Title)} [{Safe(m.SourceInstance)} / {Safe(m.Format)}]").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        return Ok(filtered.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id).Select(r => new CourseResourceRow(r.Id, kind, r.Name,
            labels.GetValueOrDefault(r.Id, []), locations.GetValueOrDefault(r.Id, []).OrderBy(l => l.TreeId).ThenBy(l => l.CategoryId).ThenBy(l => l.SkillId).ThenBy(l => l.State, StringComparer.Ordinal).ToArray(),
            !locations.ContainsKey(r.Id), kind is "challenge" or "lesson" ? 1 : contentCounts.GetValueOrDefault(r.Id, []).Count)).ToArray());
    }
    private static string Safe(string value) => new(value.Where(c => !char.IsControl(c)).Take(256).ToArray());
}
