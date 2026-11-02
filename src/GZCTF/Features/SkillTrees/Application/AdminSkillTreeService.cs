using System.Data;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.LearningPaths.Domain;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.SkillTrees.Application;

public sealed class AdminSkillTreeService(AppDbContext db, ISkillTreeCacheInvalidator cacheInvalidator)
{
    public async Task<AdminSkillTreeResponse> CreateAsync(CreateSkillTreeCommand command, CancellationToken token)
    {
        var name = command.Name.Trim();
        var iconKey = SkillTreeIconCatalog.IsSupported(command.IconKey) ? command.IconKey : SkillTreeIconCatalog.Default;

        var tree = new SkillTree
        {
            Name = name,
            Summary = command.Summary.Trim(),
            IconKey = iconKey,
            Revisions = []
        };

        var draft = new SkillTreeRevision
        {
            Status = SkillTreeRevisionStatus.Draft,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        tree.Revisions.Add(draft);

        db.SkillTrees.Add(tree);
        await db.SaveChangesAsync(token);

        return new AdminSkillTreeResponse(tree.Id, tree.Name, tree.Summary, tree.IconKey, false, true, tree.RowVersion, SkillHierarchyReader.CoverUrl(tree.CoverHash));
    }

    public async Task<SkillTreeDraftResponse?> GetDraftAsync(Guid treeId, CancellationToken token)
    {
        var tree = await db.SkillTrees
            .Include(t => t.Revisions)
                .ThenInclude(r => r.Categories)
                    .ThenInclude(cr => cr.Category)
            .FirstOrDefaultAsync(t => t.Id == treeId, token);

        if (tree is null)
            return null;

        var draft = tree.Revisions.SingleOrDefault(r => r.Status == SkillTreeRevisionStatus.Draft);
        if (draft is null && tree.CurrentPublishedRevision is not null)
        {
            var published = tree.CurrentPublishedRevision;
            draft = new SkillTreeRevision
            {
                Id = Guid.CreateVersion7(),
                SkillTreeId = tree.Id,
                SkillTree = tree,
                Status = SkillTreeRevisionStatus.Draft,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            foreach (var catRef in published.Categories.OrderBy(cr => cr.SortOrder))
            {
                draft.Categories.Add(new SkillTreeCategoryRef
                {
                    Revision = draft,
                    CategoryId = catRef.CategoryId,
                    SortOrder = catRef.SortOrder
                });
            }

            tree.Revisions.Add(draft);
            // Track the clone as added explicitly so SaveChanges inserts it instead of
            // relying on graph discovery for a ValueGeneratedNever key.
            db.Add(draft);
            await db.SaveChangesAsync(token);
        }

        if (draft is null)
            return null;

        return ToDraftResponse(tree, draft);
    }

    public async Task<SkillTreeDraftResponse?> UpdateDraftAsync(Guid treeId, UpdateSkillTreeDraftCommand command, CancellationToken token)
    {
        var tree = await db.SkillTrees
            .Include(t => t.Revisions)
                .ThenInclude(r => r.Categories)
                    .ThenInclude(cr => cr.Category)
            .FirstOrDefaultAsync(t => t.Id == treeId, token);

        if (tree is null)
            return null;

        var draft = tree.Revisions.SingleOrDefault(r => r.Status == SkillTreeRevisionStatus.Draft);
        if (draft is null)
            return null;

        if (command.RowVersion != draft.RowVersion)
            throw new SkillTreeRevisionConflictException();

        tree.Name = command.Name.Trim();
        tree.Summary = command.Summary.Trim();
        tree.IconKey = SkillTreeIconCatalog.IsSupported(command.IconKey) ? command.IconKey : SkillTreeIconCatalog.Default;
        tree.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var categoryIds = command.Categories.Select(c => c.CategoryId).ToList();
        var validIds = await db.SkillCategories
            .Where(c => categoryIds.Contains(c.Id) && c.DeletedAtUtc == null)
            .Select(c => c.Id)
            .ToListAsync(token);

        if (validIds.Count != categoryIds.Count)
            throw new SkillTreeValidationException("One or more categories are not active.");

        var sortOrders = command.Categories.Select(c => c.SortOrder).ToList();
        if (sortOrders.Distinct().Count() != sortOrders.Count)
            throw new SkillTreeValidationException("Duplicate sort orders are not allowed.");

        var categories = await db.SkillCategories
            .Where(c => validIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, token);

        draft.Categories.Clear();
        foreach (var catCmd in command.Categories.OrderBy(c => c.SortOrder))
        {
            draft.Categories.Add(new SkillTreeCategoryRef
            {
                Revision = draft,
                Category = categories[catCmd.CategoryId],
                CategoryId = catCmd.CategoryId,
                SortOrder = catCmd.SortOrder
            });
        }

        // Normalize sort order to 0..n-1
        var normalized = draft.Categories.OrderBy(cr => cr.SortOrder).ToList();
        for (var i = 0; i < normalized.Count; i++)
            normalized[i].SortOrder = i;

        // Touch the revision row so its PostgreSQL xmin changes when the graph changes.
        db.Entry(draft).Property(item => item.Status).IsModified = true;

        try
        {
            await db.SaveChangesAsync(token);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new SkillTreeRevisionConflictException();
        }

        return ToDraftResponse(tree, draft);
    }

    public async Task PublishAsync(Guid treeId, uint? rowVersion, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM \"SkillTrees\" WHERE \"Id\" = {treeId} FOR UPDATE", token);

        var tree = await db.SkillTrees
            .Include(t => t.Revisions)
            .FirstOrDefaultAsync(t => t.Id == treeId, token);

        if (tree is null)
            throw new SkillTreeNotFoundException();

        var draft = tree.Revisions.SingleOrDefault(r => r.Status == SkillTreeRevisionStatus.Draft);
        if (draft is null)
            throw new SkillTreeValidationException("The skill tree has no draft revision.");

        if (rowVersion is not { } rv || rv != draft.RowVersion)
            throw new SkillTreeRevisionConflictException();

        var oldPublished = tree.Revisions.SingleOrDefault(r => r.Status == SkillTreeRevisionStatus.Published);
        if (oldPublished is not null)
            oldPublished.Status = SkillTreeRevisionStatus.Archived;

        draft.Status = SkillTreeRevisionStatus.Published;
        draft.PublishedAtUtc = DateTimeOffset.UtcNow;
        tree.CurrentPublishedRevisionId = draft.Id;
        tree.CurrentPublishedRevision = draft;

        try
        {
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new SkillTreeRevisionConflictException();
        }

        await cacheInvalidator.InvalidateTreeAsync(treeId, token);
    }

    public async Task<SkillTreeDeleteImpactResponse?> GetDeleteImpactAsync(Guid treeId, CancellationToken token)
    {
        var tree = await db.SkillTrees
            .AsNoTracking()
            .Include(item => item.Revisions)
                .ThenInclude(revision => revision.Categories)
            .FirstOrDefaultAsync(t => t.Id == treeId, token);
        if (tree is null)
            return null;

        var isPublished = tree.CurrentPublishedRevisionId is not null;
        var categoryIds = tree.Revisions
            .Where(revision => revision.Status is SkillTreeRevisionStatus.Draft or SkillTreeRevisionStatus.Published)
            .SelectMany(revision => revision.Categories)
            .Select(reference => reference.CategoryId)
            .Distinct()
            .ToList();

        var categoryCount = await db.SkillCategories.CountAsync(c => categoryIds.Contains(c.Id) && c.DeletedAtUtc == null, token);
        var challengeCount = await SkillHierarchyReader.CategoryContents(db, categoryIds).Where(cc => cc.ChallengeId != null).Select(cc => cc.ChallengeId).Distinct().CountAsync(token);
        var lessonCount = await SkillHierarchyReader.CategoryContents(db, categoryIds).Where(cc => cc.LessonId != null).Select(cc => cc.LessonId).Distinct().CountAsync(token);
        var enrollmentCount = await db.SkillTreeEnrollments.CountAsync(e => e.SkillTreeId == treeId, token);

        return new SkillTreeDeleteImpactResponse(
            tree.Id, tree.Name, categoryCount, challengeCount, lessonCount, enrollmentCount,
            isPublished,
            isPublished || categoryCount + challengeCount + lessonCount + enrollmentCount > 0,
            tree.RowVersion);
    }

    public async Task DeleteAsync(Guid treeId, DeleteSkillTreeCommand command, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM \"SkillTrees\" WHERE \"Id\" = {treeId} FOR UPDATE", token);

        var tree = await db.SkillTrees
            .Include(t => t.Enrollments)
            .FirstOrDefaultAsync(t => t.Id == treeId, token);

        if (tree is null)
            throw new SkillTreeNotFoundException();

        var impact = await GetDeleteImpactAsync(treeId, token);
        if (impact is null)
            throw new SkillTreeNotFoundException();

        if (impact.RequiresTypedConfirmation && impact.Name != command.ConfirmationName)
            throw new SkillTreeConfirmationMismatchException();

        if (command.RowVersion != tree.RowVersion)
            throw new SkillTreeRevisionConflictException();

        tree.DeletedAtUtc = DateTimeOffset.UtcNow;

        // Clear current enrollment pointer for all users enrolled in this tree
        foreach (var enrollment in tree.Enrollments.Where(e => e.IsCurrent))
        {
            enrollment.IsCurrent = false;
        }

        try
        {
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new SkillTreeRevisionConflictException();
        }

        await cacheInvalidator.InvalidateTreeAsync(treeId, token);
    }

    public async Task<IReadOnlyList<AdminSkillTreeResponse>> ListAdminAsync(CancellationToken token)
    {
        var trees = await db.SkillTrees
            .AsNoTracking()
            .Where(t => t.DeletedAtUtc == null)
            .Include(t => t.Revisions)
            .OrderBy(t => t.Name)
            .ToListAsync(token);

        return trees.Select(t => new AdminSkillTreeResponse(
            t.Id, t.Name, t.Summary, t.IconKey,
            t.Revisions.Any(r => r.Status == SkillTreeRevisionStatus.Published),
            t.Revisions.Any(r => r.Status == SkillTreeRevisionStatus.Draft),
            t.RowVersion, SkillHierarchyReader.CoverUrl(t.CoverHash)
        )).ToArray();
    }

    public async Task<SkillTreeDetailResponse?> GetDraftPreviewAsync(Guid treeId, CancellationToken token)
    {
        var tree = await db.SkillTrees
            .AsNoTracking()
            .Include(t => t.Revisions)
                .ThenInclude(r => r.Categories)
                    .ThenInclude(cr => cr.Category)
            .FirstOrDefaultAsync(t => t.Id == treeId, token);

        if (tree is null)
            return null;

        var draft = tree.Revisions.SingleOrDefault(r => r.Status == SkillTreeRevisionStatus.Draft);
        if (draft is null)
            return null;

        return await BuildDetailAsync(tree, draft, token);
    }

    private async Task<SkillTreeDetailResponse> BuildDetailAsync(SkillTree tree, SkillTreeRevision revision, CancellationToken token)
    {
        var activeRefs = revision.Categories
            .Where(cr => cr.Category.DeletedAtUtc == null)
            .OrderBy(cr => cr.SortOrder)
            .ToList();

        var categoryIds = activeRefs.Select(cr => cr.CategoryId).ToList();

        var skills = await SkillHierarchyReader.LoadSkillsAsync(db, categoryIds, false, token);
        var categories = activeRefs.Select(reference =>
        {
            var categorySkills = skills.GetValueOrDefault(reference.CategoryId, []);
            return new SkillCategoryPublicResponse(reference.CategoryId, reference.Category.Name, reference.Category.Summary,
                reference.Category.IconKey, reference.SortOrder, SkillHierarchyReader.Flatten(categorySkills), categorySkills);
        }).ToArray();
        return new(tree.Id, tree.Name, tree.Summary, tree.IconKey, categories,
            skills.Values.SelectMany(value => value).Select(skill => skill.SkillId).Distinct().Count(), SkillHierarchyReader.CoverUrl(tree.CoverHash));
    }

    private static string PickTitle(IEnumerable<ChallengeLocalization>? localizations) =>
        PickText(localizations, x => x.Locale)?.Title ?? string.Empty;

    private static string PickTitle(IEnumerable<LessonLocalization>? localizations) =>
        PickText(localizations, x => x.Locale)?.Title ?? string.Empty;

    private static string PickSummary(IEnumerable<ChallengeLocalization>? localizations) =>
        PickText(localizations, x => x.Locale)?.Summary ?? string.Empty;

    private static T? PickText<T>(IEnumerable<T>? values, Func<T, string> locale) where T : class
    {
        if (values is null) return null;
        var list = values.ToList();
        return list.FirstOrDefault(x => locale(x).Equals("en", StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(x => locale(x).Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault();
    }

    private static SkillTreeDraftResponse ToDraftResponse(SkillTree tree, SkillTreeRevision draft)
    {
        var categories = draft.Categories
            .OrderBy(cr => cr.SortOrder)
            .Select(cr => new SkillTreeCategoryAdminResponse(
                cr.CategoryId, cr.Category.Name, cr.Category.Summary, cr.Category.IconKey, cr.SortOrder,
                cr.Category.RowVersion,
                Array.Empty<CategoryTreeReferenceResponse>(),
                Array.Empty<SkillTreeContentSummaryResponse>()))
            .ToArray();

        return new SkillTreeDraftResponse(
            tree.Id, draft.Id, tree.Name, tree.Summary, tree.IconKey,
            draft.RowVersion, categories, SkillHierarchyReader.CoverUrl(tree.CoverHash));
    }
}

public sealed record CreateSkillTreeCommand(string Name, string Summary, string IconKey);
public sealed record UpdateSkillTreeDraftCommand(
    string Name, string Summary, string IconKey, uint RowVersion,
    IReadOnlyList<SkillTreeCategoryOrderCommand> Categories);
public sealed record SkillTreeCategoryOrderCommand(Guid CategoryId, int SortOrder);
public sealed record PublishSkillTreeCommand(uint RowVersion);
public sealed record AdminSkillTreeResponse(
    Guid SkillTreeId, string Name, string Summary, string IconKey,
    bool IsPublished, bool HasDraft, uint RowVersion, string? CoverUrl = null);
public sealed record SkillTreeDraftResponse(
    Guid SkillTreeId, Guid RevisionId, string Name, string Summary, string IconKey,
    uint RowVersion, IReadOnlyList<SkillTreeCategoryAdminResponse> Categories, string? CoverUrl = null);
public sealed record SkillTreeCategoryAdminResponse(
    Guid CategoryId, string Name, string Summary, string IconKey, int SortOrder, uint RowVersion,
    IReadOnlyList<CategoryTreeReferenceResponse> Trees,
    IReadOnlyList<SkillTreeContentSummaryResponse> Contents);
public sealed record SkillTreeDeleteImpactResponse(
    Guid SkillTreeId, string Name, int CategoryCount, int ChallengeCount,
    int LessonCount, int EnrollmentCount, bool IsPublished,
    bool RequiresTypedConfirmation, uint RowVersion);
public sealed record DeleteSkillTreeCommand(string ConfirmationName, uint RowVersion);
