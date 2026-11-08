using System.Text.Json;
using GZCTF.Features.SkillTrees.Api;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.SkillTrees.Application;

public sealed class SkillTreeQueryService(AppDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<SkillTreeSummaryResponse>> ListPublishedAsync(CancellationToken token)
    {
        var versions = await (from tree in db.SkillTrees.AsNoTracking()
            join version in db.CoursePublishedVersions.AsNoTracking() on tree.CurrentPublishedRevisionId equals version.Id
            where tree.DeletedAtUtc == null select version).ToListAsync(token);
        return versions.Select(version =>
        {
            var detail = ToDetail(version.SkillTreeId, Read(version.PublicDocumentJson));
            var content = detail.Categories.SelectMany(c => c.Skills ?? []).SelectMany(s => s.Contents)
                .DistinctBy(c => (c.Kind, c.ContentId)).ToArray();
            return new SkillTreeSummaryResponse(detail.SkillTreeId, detail.Name, detail.Summary, detail.IconKey,
                detail.Categories.Count, content.Count(c => c.Kind == "challenge"), content.Count(c => c.Kind == "lesson"), detail.SkillCount, detail.CoverUrl);
        }).ToArray();
    }

    public async Task<SkillTreeDetailResponse?> GetPublishedAsync(Guid id, CancellationToken token)
    {
        var version = await (from tree in db.SkillTrees.AsNoTracking()
            join snapshot in db.CoursePublishedVersions.AsNoTracking() on tree.CurrentPublishedRevisionId equals snapshot.Id
            where tree.Id == id && tree.DeletedAtUtc == null select snapshot).SingleOrDefaultAsync(token);
        return version is null ? null : ToDetail(id, Read(version.PublicDocumentJson));
    }

    public async Task<SkillPublicResponse?> GetSkillAsync(Guid treeId, Guid categoryId, Guid skillId, CancellationToken token)
    {
        var tree = await GetPublishedAsync(treeId, token);
        return tree?.Categories.FirstOrDefault(category => category.CategoryId == categoryId)?.Skills?
            .FirstOrDefault(skill => skill.SkillId == skillId);
    }

    public static SkillTreeDetailResponse ToDetail(Guid treeId, CourseDocument document)
    {
        var categories = document.Categories.Select((category, categoryOrder) =>
        {
            var skills = category.Skills.Where(s => s.IsEnabled).Select((skill, skillOrder) =>
            {
                var contents = skill.Contents.Select((content, order) => new SkillTreeContentSummaryResponse(
                    content.ContentId, content.Kind, order, content.Title, content.Summary,
                    content.ExpectedMinutes, content.Difficulty)).ToArray();
                return new SkillPublicResponse(skill.SkillId, skill.Name, skill.Summary, skill.IconKey, skillOrder,
                    contents.Count(c => c.Kind == "challenge"), contents.Count(c => c.Kind == "lesson"), contents, true);
            }).ToArray();
            return new SkillCategoryPublicResponse(category.CategoryId, category.Name, category.Summary, category.IconKey,
                categoryOrder, SkillHierarchyReader.Flatten(skills), skills);
        }).ToArray();
        return new(treeId, document.Name, document.Summary, document.IconKey, categories,
            categories.SelectMany(c => c.Skills ?? []).Select(s => s.SkillId).Distinct().Count(), document.CoverUrl);
    }
    private static CourseDocument Read(string json) => JsonSerializer.Deserialize<CourseDocument>(json, JsonOptions)
        ?? throw new InvalidOperationException("Invalid published course document.");
}
