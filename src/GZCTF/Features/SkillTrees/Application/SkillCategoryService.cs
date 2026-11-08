using System.Data;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.LearningPaths.Domain;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.SkillTrees.Application;

public sealed class SkillCategoryService(AppDbContext db, ISkillTreeCacheInvalidator cacheInvalidator)
{
    private const int MaxNameLength = 128;
    private const int MaxSummaryLength = 1024;
    private const string ChallengeKind = "challenge";
    private const string LessonKind = "lesson";

    public async Task<IReadOnlyList<SkillCategoryAdminResponse>> ListAsync(CancellationToken token)
    {
        var categories = await db.SkillCategories
            .AsNoTracking()
            .Where(category => category.DeletedAtUtc == null)
            .OrderBy(category => category.Name)
            .ToListAsync(token);

        if (categories.Count == 0)
            return [];

        var ids = categories.Select(category => category.Id).ToList();
        var treeRefs = await LoadTreeReferencesAsync(ids, token);
        var contents = await LoadContentSummariesAsync(ids, token);
        var skills = await SkillHierarchyReader.LoadSkillsAsync(db, ids, false, token);

        return categories.Select(category => ToAdminResponse(
            category,
            treeRefs.GetValueOrDefault(category.Id, []),
            contents.GetValueOrDefault(category.Id, []), skills.GetValueOrDefault(category.Id, []))).ToArray();
    }

    public async Task<SkillCategoryAdminResponse?> GetAsync(Guid id, CancellationToken token)
    {
        var category = await db.SkillCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id && item.DeletedAtUtc == null, token);
        if (category is null)
            return null;

        var treeRefs = await LoadTreeReferencesAsync([id], token);
        var contents = await LoadContentSummariesAsync([id], token);
        var skills = await SkillHierarchyReader.LoadSkillsAsync(db, [id], false, token);
        return ToAdminResponse(
            category,
            treeRefs.GetValueOrDefault(id, []),
            contents.GetValueOrDefault(id, []), skills.GetValueOrDefault(id, []));
    }

    public async Task<SkillCategoryAdminResponse> CreateAsync(
        SkillCategoryCommand command, CancellationToken token)
    {
        var name = ValidateName(command.Name);
        var summary = ValidateSummary(command.Summary);
        var iconKey = ValidateIcon(command.IconKey);

        var category = new SkillCategory
        {
            Name = name,
            Summary = summary,
            IconKey = iconKey
        };

        db.SkillCategories.Add(category);
        await db.SaveChangesAsync(token);
        return ToAdminResponse(category, [], []);
    }

    public async Task<SkillCategoryAdminResponse?> UpdateAsync(
        Guid id, SkillCategoryCommand command, CancellationToken token)
    {
        var category = await db.SkillCategories
            .FirstOrDefaultAsync(item => item.Id == id && item.DeletedAtUtc == null, token);
        if (category is null)
            return null;

        if (command.RowVersion is { } rowVersion && rowVersion != category.RowVersion)
            throw new SkillTreeRevisionConflictException();

        category.Name = ValidateName(command.Name);
        category.Summary = ValidateSummary(command.Summary);
        category.IconKey = ValidateIcon(command.IconKey);
        category.UpdatedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync(token);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new SkillTreeRevisionConflictException();
        }

        return await GetAsync(id, token);
    }

    public async Task<SkillCategoryAdminResponse?> UpdateContentsAsync(
        Guid id, UpdateCategoryContentsCommand command, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var category = await db.SkillCategories.FirstOrDefaultAsync(item => item.Id == id && item.DeletedAtUtc == null, token);
        if (category is null) return null;
        if (category.RowVersion != command.RowVersion) throw new SkillTreeRevisionConflictException();
        var skillIds = await db.CategorySkillRefs.Where(reference => reference.CategoryId == id && reference.Skill.DeletedAtUtc == null)
            .Select(reference => reference.SkillId).ToListAsync(token);
        if (skillIds.Count != 1) throw new SkillSelectionRequiredException();
        var skill = await db.Skills.SingleAsync(item => item.Id == skillIds[0], token);
        await new SkillService(db, cacheInvalidator).UpdateContentsAsync(skill.Id,
            new SkillContentUpdateCommand(skill.RowVersion, command.Contents), token);
        category.UpdatedAtUtc = DateTimeOffset.UtcNow;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new SkillTreeRevisionConflictException(); }
        await transaction.CommitAsync(token);
        return await GetAsync(id, token);
    }

    public async Task<UpdateCategoryTreeMembershipsResponse?> UpdateTreeMembershipsAsync(
        Guid id, UpdateCategoryTreeMembershipsCommand command, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);

        var category = await db.SkillCategories
            .FirstOrDefaultAsync(item => item.Id == id && item.DeletedAtUtc == null, token);
        if (category is null)
            return null;

        if (command.CategoryRowVersion != category.RowVersion)
            throw new SkillTreeRevisionConflictException();

        var treeIds = (command.Trees ?? []).Select(item => item.SkillTreeId).Distinct().ToList();
        foreach (var treeId in treeIds.OrderBy(item => item))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT \"Id\" FROM \"SkillTrees\" WHERE \"Id\" = {treeId} FOR UPDATE", token);

        var trees = await db.SkillTrees
            .Include(tree => tree.Revisions)
                .ThenInclude(revision => revision.Categories)
            .Where(tree => treeIds.Contains(tree.Id) && tree.DeletedAtUtc == null)
            .ToListAsync(token);

        if (trees.Count != treeIds.Count)
            throw new SkillCategoryValidationException("One or more skill trees were not found.");

        var affected = new List<Guid>();
        foreach (var membership in command.Trees ?? [])
        {
            var tree = trees.Single(item => item.Id == membership.SkillTreeId);
            if (membership.SkillTreeRowVersion != tree.RowVersion)
                throw new SkillTreeRevisionConflictException();

            var draft = tree.Revisions.SingleOrDefault(revision => revision.Status == SkillTreeRevisionStatus.Draft);

            if (membership.Included)
            {
                if (draft is null)
                {
                    draft = ClonePublishedRevision(tree);
                    tree.Revisions.Add(draft);
                    // Entry(...) below would force-attach the fresh clone as an existing row
                    // and turn the insert into a zero-row xmin update, so track it as added.
                    db.Add(draft);
                }

                if (draft.Categories.All(reference => reference.CategoryId != id))
                {
                    draft.Categories.Add(new SkillTreeCategoryRef
                    {
                        Revision = draft,
                        CategoryId = id,
                        SortOrder = draft.Categories.Count
                    });
                }

                NormalizeOrder(draft);
            }
            else
            {
                if (draft is null && tree.Revisions.Any(revision =>
                        revision.Status == SkillTreeRevisionStatus.Published))
                {
                    draft = ClonePublishedRevision(tree);
                    tree.Revisions.Add(draft);
                    db.Add(draft);
                }

                if (draft is not null)
                {
                    var removed = draft.Categories.Where(reference => reference.CategoryId == id).ToList();
                    foreach (var reference in removed)
                        draft.Categories.Remove(reference);
                    NormalizeOrder(draft);
                }
            }

            if (draft is not null)
                db.Entry(draft).Property(revision => revision.Status).IsModified = true;

            affected.Add(tree.Id);
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

        foreach (var treeId in affected)
            await cacheInvalidator.InvalidateTreeAsync(treeId, token);

        return new UpdateCategoryTreeMembershipsResponse(id, affected);
    }

    public async Task<CategoryDeleteImpactResponse?> GetDeleteImpactAsync(Guid id, CancellationToken token)
    {
        var category = await db.SkillCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id && item.DeletedAtUtc == null, token);
        if (category is null)
            return null;

        var references = await db.SkillTreeCategoryRefs
            .AsNoTracking()
            .Where(reference => reference.CategoryId == id &&
                                reference.Revision.SkillTree.DeletedAtUtc == null)
            .Select(reference => new
            {
                reference.Revision.SkillTreeId,
                IsDraft = reference.Revision.Status == SkillTreeRevisionStatus.Draft,
                IsPublished = reference.Revision.SkillTree.CurrentPublishedRevisionId == reference.RevisionId
            })
            .ToListAsync(token);

        var draftTreeCount = references.Where(item => item.IsDraft)
            .Select(item => item.SkillTreeId).Distinct().Count();
        var publishedTreeCount = references.Where(item => item.IsPublished)
            .Select(item => item.SkillTreeId).Distinct().Count();
        var challengeCount = await SkillHierarchyReader.CategoryContents(db, [id]).Where(item => item.ChallengeId != null).Select(item => item.ChallengeId).Distinct().CountAsync(token);
        var lessonCount = await SkillHierarchyReader.CategoryContents(db, [id]).Where(item => item.LessonId != null).Select(item => item.LessonId).Distinct().CountAsync(token);

        return new CategoryDeleteImpactResponse(
            category.Id, category.Name, draftTreeCount, publishedTreeCount,
            challengeCount, lessonCount,
            draftTreeCount + publishedTreeCount + challengeCount + lessonCount > 0,
            category.RowVersion);
    }

    public async Task DeleteAsync(Guid id, DeleteCategoryCommand command, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM \"SkillCategories\" WHERE \"Id\" = {id} FOR UPDATE", token);

        var category = await db.SkillCategories
            .FirstOrDefaultAsync(item => item.Id == id && item.DeletedAtUtc == null, token);
        if (category is null)
            throw new SkillCategoryNotFoundException();

        var impact = await GetDeleteImpactAsync(id, token);
        if (impact is null)
            throw new SkillCategoryNotFoundException();

        if (impact.RequiresTypedConfirmation && category.Name != (command.ConfirmationName ?? string.Empty).Trim())
            throw new SkillCategoryConfirmationMismatchException();

        if (command.RowVersion != category.RowVersion)
            throw new SkillTreeRevisionConflictException();

        var draftRevisionIds = await db.SkillTreeCategoryRefs
            .Where(reference => reference.CategoryId == id &&
                                reference.Revision.Status == SkillTreeRevisionStatus.Draft)
            .Select(reference => reference.RevisionId)
            .Distinct()
            .ToListAsync(token);

        var referencingTreeIds = await db.SkillTreeCategoryRefs
            .Where(reference => reference.CategoryId == id)
            .Select(reference => reference.Revision.SkillTreeId)
            .Distinct()
            .ToListAsync(token);

        category.DeletedAtUtc = DateTimeOffset.UtcNow;
        category.UpdatedAtUtc = DateTimeOffset.UtcNow;

        foreach (var revisionId in draftRevisionIds)
        {
            var references = await db.SkillTreeCategoryRefs
                .Where(reference => reference.RevisionId == revisionId && reference.CategoryId != id)
                .OrderBy(reference => reference.SortOrder)
                .ToListAsync(token);
            await db.SkillTreeCategoryRefs
                .Where(reference => reference.RevisionId == revisionId)
                .ExecuteDeleteAsync(token);
            for (var sortOrder = 0; sortOrder < references.Count; sortOrder++)
            {
                db.SkillTreeCategoryRefs.Add(new SkillTreeCategoryRef
                {
                    RevisionId = revisionId,
                    CategoryId = references[sortOrder].CategoryId,
                    SortOrder = sortOrder
                });
            }
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

        foreach (var treeId in referencingTreeIds)
            await cacheInvalidator.InvalidateTreeAsync(treeId, token);
    }

    public async Task MergeAsync(MergeSkillCategoryCommand command, CancellationToken token)
    {
        if (command.SurvivorCategoryId == command.DuplicateCategoryId)
            throw new SkillCategoryMergeConflictException("A category cannot be merged into itself.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);

        foreach (var id in new[] { command.SurvivorCategoryId, command.DuplicateCategoryId }.OrderBy(item => item))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT \"Id\" FROM \"SkillCategories\" WHERE \"Id\" = {id} FOR UPDATE", token);

        var survivor = await db.SkillCategories
            .FirstOrDefaultAsync(item => item.Id == command.SurvivorCategoryId && item.DeletedAtUtc == null, token)
            ?? throw new SkillCategoryMergeConflictException("The survivor category was not found.");
        var duplicate = await db.SkillCategories
            .FirstOrDefaultAsync(item => item.Id == command.DuplicateCategoryId && item.DeletedAtUtc == null, token)
            ?? throw new SkillCategoryMergeConflictException("The duplicate category was not found.");

        if (command.SurvivorRowVersion != survivor.RowVersion ||
            command.DuplicateRowVersion != duplicate.RowVersion)
            throw new SkillTreeRevisionConflictException();

        var referencingTreeIds = await db.SkillTreeCategoryRefs
            .Where(reference => reference.CategoryId == command.SurvivorCategoryId ||
                                reference.CategoryId == command.DuplicateCategoryId)
            .Select(reference => reference.Revision.SkillTreeId)
            .Distinct()
            .ToListAsync(token);

        var revisionIds = await db.SkillTreeCategoryRefs
            .Where(reference => reference.CategoryId == command.DuplicateCategoryId)
            .Select(reference => reference.RevisionId)
            .Distinct()
            .ToListAsync(token);

        foreach (var revisionId in revisionIds)
        {
            var references = await db.SkillTreeCategoryRefs
                .Where(reference => reference.RevisionId == revisionId)
                .OrderBy(reference => reference.SortOrder)
                .ToListAsync(token);
            var finalCategories = new List<Guid>();
            foreach (var reference in references)
            {
                var categoryId = reference.CategoryId == command.DuplicateCategoryId
                    ? command.SurvivorCategoryId
                    : reference.CategoryId;
                if (!finalCategories.Contains(categoryId))
                    finalCategories.Add(categoryId);
            }

            await db.SkillTreeCategoryRefs
                .Where(reference => reference.RevisionId == revisionId)
                .ExecuteDeleteAsync(token);
            for (var sortOrder = 0; sortOrder < finalCategories.Count; sortOrder++)
                db.SkillTreeCategoryRefs.Add(new SkillTreeCategoryRef
                {
                    RevisionId = revisionId,
                    CategoryId = finalCategories[sortOrder],
                    SortOrder = sortOrder
                });
        }

        var survivorSkills = await db.CategorySkillRefs.Where(reference => reference.CategoryId == command.SurvivorCategoryId)
            .OrderBy(reference => reference.SortOrder).Select(reference => reference.SkillId).ToListAsync(token);
        var duplicateSkills = await db.CategorySkillRefs.Where(reference => reference.CategoryId == command.DuplicateCategoryId)
            .OrderBy(reference => reference.SortOrder).Select(reference => reference.SkillId).ToListAsync(token);
        var finalSkills = survivorSkills.Concat(duplicateSkills).Distinct().ToList();
        await db.CategorySkillRefs.Where(reference => reference.CategoryId == command.SurvivorCategoryId || reference.CategoryId == command.DuplicateCategoryId)
            .ExecuteDeleteAsync(token);
        for (var index = 0; index < finalSkills.Count; index++)
            db.CategorySkillRefs.Add(new CategorySkillRef { CategoryId = command.SurvivorCategoryId, SkillId = finalSkills[index], SortOrder = index });

        var now = DateTimeOffset.UtcNow;
        survivor.UpdatedAtUtc = now;
        duplicate.DeletedAtUtc = now;
        duplicate.MergedIntoCategoryId = command.SurvivorCategoryId;
        duplicate.UpdatedAtUtc = now;

        try
        {
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new SkillTreeRevisionConflictException();
        }
        catch (DbUpdateException exception)
        {
            throw new SkillCategoryMergeConflictException(exception.Message);
        }

        await cacheInvalidator.InvalidateByCategoryAsync(command.SurvivorCategoryId, token);
        foreach (var treeId in referencingTreeIds)
            await cacheInvalidator.InvalidateTreeAsync(treeId, token);
    }

    private async Task<Dictionary<Guid, IReadOnlyList<CategoryTreeReferenceResponse>>> LoadTreeReferencesAsync(
        IReadOnlyList<Guid> categoryIds, CancellationToken token)
    {
        var references = await db.SkillTreeCategoryRefs
            .AsNoTracking()
            .Where(reference => categoryIds.Contains(reference.CategoryId) &&
                                reference.Revision.SkillTree.DeletedAtUtc == null &&
                                (reference.Revision.Status == SkillTreeRevisionStatus.Draft ||
                                 reference.Revision.SkillTree.CurrentPublishedRevisionId == reference.RevisionId))
            .Select(reference => new
            {
                reference.CategoryId,
                reference.Revision.SkillTreeId,
                reference.Revision.SkillTree.Name,
                IsPublished = reference.Revision.SkillTree.CurrentPublishedRevisionId == reference.RevisionId
            })
            .ToListAsync(token);

        return references
            .GroupBy(item => item.CategoryId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CategoryTreeReferenceResponse>)group
                    .GroupBy(item => item.SkillTreeId)
                    .Select(tree => new CategoryTreeReferenceResponse(
                        tree.Key,
                        tree.First().Name,
                        tree.Any(item => item.IsPublished)))
                    .OrderBy(item => item.Name)
                    .ToArray());
    }

    private async Task<Dictionary<Guid, IReadOnlyList<SkillTreeContentSummaryResponse>>> LoadContentSummariesAsync(
        IReadOnlyList<Guid> categoryIds, CancellationToken token)
    {
        var skills = await SkillHierarchyReader.LoadSkillsAsync(db, categoryIds, false, token);
        return skills.ToDictionary(pair => pair.Key, pair => SkillHierarchyReader.Flatten(pair.Value));
    }

    private static SkillTreeContentSummaryResponse ToContentSummary(CategoryContent content)
    {
        if (content.ChallengeId is { } challengeId && content.Challenge is { } challenge)
            return new SkillTreeContentSummaryResponse(
                challengeId, ChallengeKind, content.SortOrder,
                PickTitle(challenge.Localizations), PickSummary(challenge.Localizations),
                challenge.ExpectedMinutes, challenge.Difficulty.ToString(),
                challenge.PublicationState.ToString());

        if (content.LessonId is { } lessonId && content.Lesson is { } lesson)
            return new SkillTreeContentSummaryResponse(
                lessonId, LessonKind, content.SortOrder,
                PickTitle(lesson.Localizations), string.Empty, 0, string.Empty,
                lesson.PublicationState.ToString());

        return new SkillTreeContentSummaryResponse(
            content.ChallengeId ?? content.LessonId ?? Guid.Empty, "unknown", content.SortOrder,
            string.Empty, string.Empty, 0, string.Empty);
    }

    private static SkillTreeRevision ClonePublishedRevision(SkillTree tree)
    {
        var published = tree.Revisions
            .OrderByDescending(revision => revision.PublishedAtUtc ?? revision.CreatedAtUtc)
            .FirstOrDefault(revision => revision.Status == SkillTreeRevisionStatus.Published);

        var draft = new SkillTreeRevision
        {
            SkillTreeId = tree.Id,
            SkillTree = tree,
            Status = SkillTreeRevisionStatus.Draft,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        if (published is not null)
            foreach (var reference in published.Categories.OrderBy(item => item.SortOrder))
                draft.Categories.Add(new SkillTreeCategoryRef
                {
                    Revision = draft,
                    CategoryId = reference.CategoryId,
                    SortOrder = reference.SortOrder
                });

        return draft;
    }

    private static void NormalizeOrder(SkillTreeRevision revision)
    {
        var ordered = revision.Categories.OrderBy(reference => reference.SortOrder).ToList();
        for (var index = 0; index < ordered.Count; index++)
            ordered[index].SortOrder = index;
    }

    private static string ContentKey(Guid? challengeId, Guid? lessonId) =>
        challengeId is not null ? $"{ChallengeKind}:{challengeId}" : $"{LessonKind}:{lessonId}";

    private static string NormalizeKind(string? kind) =>
        string.Equals(kind, ChallengeKind, StringComparison.OrdinalIgnoreCase) ? ChallengeKind : LessonKind;

    private static string ValidateName(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new SkillCategoryValidationException("A category name is required.");
        if (name.Length > MaxNameLength)
            throw new SkillCategoryValidationException(
                $"A category name cannot exceed {MaxNameLength} characters.");
        return name;
    }

    private static string ValidateSummary(string? value)
    {
        var summary = (value ?? string.Empty).Trim();
        if (summary.Length > MaxSummaryLength)
            throw new SkillCategoryValidationException(
                $"A category summary cannot exceed {MaxSummaryLength} characters.");
        return summary;
    }

    private static string ValidateIcon(string? value) =>
        SkillTreeIconCatalog.IsSupported(value)
            ? value!
            : throw new SkillTreeInvalidIconException();

    private static SkillCategoryAdminResponse ToAdminResponse(
        SkillCategory category,
        IReadOnlyList<CategoryTreeReferenceResponse> trees,
        IReadOnlyList<SkillTreeContentSummaryResponse> contents, IReadOnlyList<SkillPublicResponse>? skills = null) =>
        new(category.Id, category.Name, category.Summary, category.IconKey, category.RowVersion, trees, contents, skills ?? []);

    private static string PickTitle(IEnumerable<ChallengeLocalization>? localizations) =>
        PickText(localizations, item => item.Locale)?.Title ?? string.Empty;

    private static string PickTitle(IEnumerable<LessonLocalization>? localizations) =>
        PickText(localizations, item => item.Locale)?.Title ?? string.Empty;

    private static string PickSummary(IEnumerable<ChallengeLocalization>? localizations) =>
        PickText(localizations, item => item.Locale)?.Summary ?? string.Empty;

    private static T? PickText<T>(IEnumerable<T>? values, Func<T, string> locale) where T : class
    {
        if (values is null)
            return null;
        var list = values.ToList();
        return list.FirstOrDefault(item => locale(item).Equals("en", StringComparison.OrdinalIgnoreCase))
               ?? list.FirstOrDefault(item => locale(item).Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
               ?? list.FirstOrDefault();
    }
}
