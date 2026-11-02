using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.SkillTrees.Application;

public sealed class SkillTreeQueryService(AppDbContext db)
{
    public async Task<IReadOnlyList<SkillTreeSummaryResponse>> ListPublishedAsync(CancellationToken token)
    {
        var trees = await db.SkillTrees.AsNoTracking().AsSplitQuery().Include(tree => tree.CurrentPublishedRevision!)
            .ThenInclude(revision => revision.Categories).ThenInclude(reference => reference.Category)
            .Where(tree => tree.DeletedAtUtc == null && tree.CurrentPublishedRevisionId != null).ToListAsync(token);
        var categoryIds = trees.SelectMany(tree => tree.CurrentPublishedRevision!.Categories)
            .Where(reference => reference.Category.DeletedAtUtc == null).Select(reference => reference.CategoryId).Distinct().ToList();
        var skills = await SkillHierarchyReader.LoadSkillsAsync(db, categoryIds, true, token);
        return trees.Select(tree =>
        {
            var categories = tree.CurrentPublishedRevision!.Categories.Where(reference => reference.Category.DeletedAtUtc == null).ToList();
            var treeSkills = categories.SelectMany(reference => skills.GetValueOrDefault(reference.CategoryId, []))
                .DistinctBy(skill => skill.SkillId).ToArray();
            var contents = treeSkills.SelectMany(skill => skill.Contents).DistinctBy(content => (content.Kind, content.ContentId)).ToList();
            return new SkillTreeSummaryResponse(tree.Id, tree.Name, tree.Summary, tree.IconKey, categories.Count,
                contents.Count(content => content.Kind == "challenge"), contents.Count(content => content.Kind == "lesson"),
                treeSkills.Length, SkillHierarchyReader.CoverUrl(tree.CoverHash));
        }).ToArray();
    }

    public async Task<SkillTreeDetailResponse?> GetPublishedAsync(Guid id, CancellationToken token)
    {
        var tree = await db.SkillTrees.AsNoTracking().Include(tree => tree.CurrentPublishedRevision!)
            .ThenInclude(revision => revision.Categories).ThenInclude(reference => reference.Category)
            .FirstOrDefaultAsync(tree => tree.Id == id && tree.DeletedAtUtc == null && tree.CurrentPublishedRevisionId != null, token);
        if (tree?.CurrentPublishedRevision is null) return null;
        var references = tree.CurrentPublishedRevision.Categories.Where(reference => reference.Category.DeletedAtUtc == null)
            .OrderBy(reference => reference.SortOrder).ToList();
        var skills = await SkillHierarchyReader.LoadSkillsAsync(db, references.Select(reference => reference.CategoryId).ToList(), true, token);
        var categories = references.Select(reference =>
        {
            var categorySkills = skills.GetValueOrDefault(reference.CategoryId, []);
            return new SkillCategoryPublicResponse(reference.CategoryId, reference.Category.Name, reference.Category.Summary,
                reference.Category.IconKey, reference.SortOrder, SkillHierarchyReader.Flatten(categorySkills), categorySkills);
        }).ToArray();
        return new(tree.Id, tree.Name, tree.Summary, tree.IconKey, categories,
            skills.Values.SelectMany(value => value).Select(skill => skill.SkillId).Distinct().Count(), SkillHierarchyReader.CoverUrl(tree.CoverHash));
    }

    public async Task<SkillPublicResponse?> GetSkillAsync(Guid treeId, Guid categoryId, Guid skillId, CancellationToken token)
    {
        var tree = await GetPublishedAsync(treeId, token);
        return tree?.Categories.FirstOrDefault(category => category.CategoryId == categoryId)?.Skills?
            .FirstOrDefault(skill => skill.SkillId == skillId);
    }
}
