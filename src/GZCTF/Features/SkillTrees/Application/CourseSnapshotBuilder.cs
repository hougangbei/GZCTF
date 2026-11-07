using System.Text.Json;
using GZCTF.Features.ChallengeRuntime.Application;
using GZCTF.Features.SkillTrees.Api;
using GZCTF.Features.SkillTrees.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.SkillTrees.Application;

public static class CourseSnapshotBuilder
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Capture a legacy graph at its explicit publication boundary. Caller commits the changes.</summary>
    public static async Task<CoursePublishedVersion> CaptureCurrentAsync(AppDbContext db, Guid treeId, Guid revisionId, CancellationToken token)
    {
        var tree = await db.SkillTrees.AsNoTracking().SingleAsync(t => t.Id == treeId, token);
        var refs = await db.SkillTreeCategoryRefs.AsNoTracking().Include(r => r.Category)
            .Where(r => r.RevisionId == revisionId && r.Category.DeletedAtUtc == null).OrderBy(r => r.SortOrder).ToArrayAsync(token);
        var skills = await SkillHierarchyReader.LoadSkillsAsync(db, refs.Select(r => r.CategoryId).ToArray(), true, token);
        var doc = new CourseDocument(tree.Name, tree.Summary, tree.IconKey, SkillHierarchyReader.CoverUrl(tree.CoverHash),
            refs.Select(r => new CourseCategory(r.CategoryId, r.Category.Name, r.Category.Summary, r.Category.IconKey,
                skills.GetValueOrDefault(r.CategoryId, []).Select(s => new CourseSkill(s.SkillId, s.Name, s.Summary, s.IconKey,
                    s.IsEnabled, s.Contents.Select(c => new CourseContent(c.ContentId, c.Kind, c.Title, c.Summary, null, c.ExpectedMinutes, c.Difficulty)).ToList())).ToList())).ToList());
        var result = new CoursePublishedVersion { Id = revisionId, SkillTreeId = treeId, PublishedAtUtc = DateTimeOffset.UtcNow,
            PublicDocumentJson = JsonSerializer.Serialize(doc, Options) };
        var all = doc.Categories.SelectMany(c => c.Skills).SelectMany(s => s.Contents).DistinctBy(c => (c.Kind, c.ContentId)).ToArray();
        var challengeIds = all.Where(c => c.Kind == "challenge").Select(c => c.ContentId).ToArray();
        var lessonIds = all.Where(c => c.Kind == "lesson").Select(c => c.ContentId).ToArray();
        var challenges = await db.Challenges.AsNoTracking().AsSplitQuery().Include(c => c.Localizations).Include(c => c.Flags)
            .Include(c => c.Hints).Include(c => c.Writeups).Where(c => challengeIds.Contains(c.Id)).ToArrayAsync(token);
        var lessons = await db.Lessons.AsNoTracking().Include(l => l.Localizations).Where(l => lessonIds.Contains(l.Id)).ToArrayAsync(token);
        result.Contents.AddRange(challenges.Select(c => new CoursePublishedContent { Id = Guid.CreateVersion7(), VersionId = revisionId,
            ContentId = c.Id, Kind = "challenge", PayloadJson = CoursePublicationService.RewriteAssetUrls(PublishedCourseContentReader.FreezeChallenge(c), treeId, revisionId, "challenge", c.Id) }));
        result.Contents.AddRange(lessons.Select(l => new CoursePublishedContent { Id = Guid.CreateVersion7(), VersionId = revisionId,
            ContentId = l.Id, Kind = "lesson", PayloadJson = CoursePublicationService.RewriteAssetUrls(CourseWorkspaceContentService.FreezeLesson(l), treeId, revisionId, "lesson", l.Id) }));
        db.CoursePublishedVersions.Add(result);
        return result;
    }
}
