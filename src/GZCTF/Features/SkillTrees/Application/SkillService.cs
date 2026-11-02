using System.Data;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.LearningPaths.Domain;
using GZCTF.Features.SkillTrees.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.SkillTrees.Application;

public sealed class SkillService(AppDbContext db, ISkillTreeCacheInvalidator cache)
{
    public async Task<IReadOnlyList<SkillAdminResponse>> ListAsync(CancellationToken token)
    {
        var skills = await Query().OrderBy(skill => skill.Name).ToListAsync(token);
        return skills.Select(ToResponse).ToArray();
    }
    public async Task<SkillAdminResponse?> GetAsync(Guid id, CancellationToken token) =>
        await Query().FirstOrDefaultAsync(skill => skill.Id == id, token) is { } skill ? ToResponse(skill) : null;

    private IQueryable<Skill> Query() => db.Skills.AsNoTracking().AsSplitQuery()
        .Include(skill => skill.Categories).ThenInclude(reference => reference.Category)
        .Include(skill => skill.Contents).ThenInclude(content => content.Challenge!).ThenInclude(challenge => challenge.Localizations)
        .Include(skill => skill.Contents).ThenInclude(content => content.Lesson!).ThenInclude(lesson => lesson.Localizations)
        .Where(skill => skill.DeletedAtUtc == null);

    public async Task<SkillAdminResponse> CreateAsync(SkillCommand command, CancellationToken token)
    {
        ValidateMetadata(command);
        var skill = new Skill { Name = command.Name.Trim(), Summary = (command.Summary ?? "").Trim(), IconKey = command.IconKey, IsEnabled = command.IsEnabled };
        db.Skills.Add(skill);
        await SaveAsync(token);
        return (await GetAsync(skill.Id, token))!;
    }
    public async Task<SkillAdminResponse?> UpdateAsync(Guid id, SkillCommand command, CancellationToken token)
    {
        ValidateMetadata(command);
        var skill = await db.Skills.FirstOrDefaultAsync(skill => skill.Id == id && skill.DeletedAtUtc == null, token);
        if (skill is null) return null;
        RequireVersion(command.RowVersion, skill.RowVersion);
        skill.Name = command.Name.Trim(); skill.Summary = (command.Summary ?? "").Trim(); skill.IconKey = command.IconKey;
        skill.IsEnabled = command.IsEnabled; skill.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await SaveAsync(token);
        await InvalidateAsync(id, token);
        return await GetAsync(id, token);
    }
    public async Task<SkillAdminResponse?> UpdateContentsAsync(Guid id, SkillContentUpdateCommand command, CancellationToken token)
    {
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
        var skill = await db.Skills.FirstOrDefaultAsync(skill => skill.Id == id && skill.DeletedAtUtc == null, token);
        if (skill is null) return null;
        RequireVersion(command.RowVersion, skill.RowVersion);
        var contents = command.Contents ?? [];
        if (contents.Any(content => content.SortOrder < 0 || content.Kind is not ("lesson" or "challenge")) ||
            contents.DistinctBy(content => content.SortOrder).Count() != contents.Count ||
            contents.DistinctBy(content => (content.Kind, content.ContentId)).Count() != contents.Count)
            throw new SkillCategoryValidationException("Content kinds, duplicate entries and sort orders must be valid.");
        var challengeIds = contents.Where(content => content.Kind == "challenge").Select(content => content.ContentId).ToList();
        var lessonIds = contents.Where(content => content.Kind == "lesson").Select(content => content.ContentId).ToList();
        if (await db.Challenges.CountAsync(challenge => challengeIds.Contains(challenge.Id) &&
                challenge.PublicationState != ChallengePublicationState.Retired && challenge.PublicationState != ChallengePublicationState.Merged, token) != challengeIds.Count ||
            await db.Lessons.CountAsync(lesson => lessonIds.Contains(lesson.Id) && lesson.PublicationState != LessonPublicationState.Retired, token) != lessonIds.Count)
            throw new SkillCategoryValidationException("A referenced content item is missing or retired.");
        await db.SkillContents.Where(content => content.SkillId == id).ExecuteDeleteAsync(token);
        foreach (var content in contents)
            db.SkillContents.Add(new SkillContent { SkillId = id, SortOrder = content.SortOrder,
                ChallengeId = content.Kind == "challenge" ? content.ContentId : null, LessonId = content.Kind == "lesson" ? content.ContentId : null });
        skill.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await SaveAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        await InvalidateAsync(id, token);
        return await GetAsync(id, token);
    }
    public async Task<SkillCategoryAdminResponse?> UpdateCategorySkillsAsync(Guid id, CategorySkillsUpdateCommand command, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var category = await db.SkillCategories.FirstOrDefaultAsync(category => category.Id == id && category.DeletedAtUtc == null, token);
        if (category is null) return null;
        RequireVersion(command.RowVersion, category.RowVersion);
        var skills = command.Skills ?? [];
        if (skills.Any(skill => skill.SortOrder < 0) || skills.DistinctBy(skill => skill.SkillId).Count() != skills.Count ||
            skills.DistinctBy(skill => skill.SortOrder).Count() != skills.Count)
            throw new SkillCategoryValidationException("Duplicate skills or invalid sort orders are not allowed.");
        var ids = skills.Select(skill => skill.SkillId).ToList();
        if (await db.Skills.CountAsync(skill => ids.Contains(skill.Id) && skill.DeletedAtUtc == null, token) != ids.Count)
            throw new SkillCategoryValidationException("A referenced skill is missing or deleted.");
        await db.CategorySkillRefs.Where(reference => reference.CategoryId == id).ExecuteDeleteAsync(token);
        foreach (var skill in skills) db.CategorySkillRefs.Add(new CategorySkillRef { CategoryId = id, SkillId = skill.SkillId, SortOrder = skill.SortOrder });
        category.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await SaveAsync(token); await transaction.CommitAsync(token);
        await cache.InvalidateByCategoryAsync(id, token);
        return await new SkillCategoryService(db, cache).GetAsync(id, token);
    }
    public async Task<SkillDeleteImpactResponse?> GetDeleteImpactAsync(Guid id, CancellationToken token)
    {
        var skill = await GetAsync(id, token);
        return skill is null ? null : new(id, skill.Name, skill.Categories.Count,
            skill.Contents.Count(content => content.Kind == "challenge"), skill.Contents.Count(content => content.Kind == "lesson"),
            skill.Categories.Count + skill.Contents.Count > 0, skill.RowVersion);
    }
    public async Task<bool> DeleteAsync(Guid id, DeleteSkillCommand command, CancellationToken token)
    {
        var skill = await db.Skills.FirstOrDefaultAsync(skill => skill.Id == id && skill.DeletedAtUtc == null, token);
        if (skill is null) return false;
        RequireVersion(command.RowVersion, skill.RowVersion);
        var impact = (await GetDeleteImpactAsync(id, token))!;
        if (impact.RequiresTypedConfirmation && command.ConfirmationName?.Trim() != skill.Name)
            throw new SkillTreeConfirmationMismatchException();
        skill.DeletedAtUtc = skill.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await SaveAsync(token); await InvalidateAsync(id, token); return true;
    }
    public async Task InvalidateAsync(Guid id, CancellationToken token)
    {
        foreach (var categoryId in await db.CategorySkillRefs.Where(reference => reference.SkillId == id).Select(reference => reference.CategoryId).Distinct().ToListAsync(token))
            await cache.InvalidateByCategoryAsync(categoryId, token);
    }
    private async Task SaveAsync(CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new SkillTreeRevisionConflictException(); }
    }
    internal static void RequireVersion(uint? version, uint actual)
    {
        if (version is null || version != actual) throw new SkillTreeRevisionConflictException();
    }
    internal static void ValidateMetadata(SkillCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 128 || (command.Summary ?? "").Trim().Length > 1024)
            throw new SkillCategoryValidationException("A name of 1–128 characters and summary of at most 1024 characters are required.");
        if (!SkillTreeIconCatalog.IsSupported(command.IconKey)) throw new SkillTreeInvalidIconException();
    }
    private static SkillAdminResponse ToResponse(Skill skill) => new(skill.Id, skill.Name, skill.Summary, skill.IconKey,
        skill.IsEnabled, skill.RowVersion, skill.Categories.Where(reference => reference.Category.DeletedAtUtc == null)
            .Select(reference => new SkillCategoryReferenceResponse(reference.CategoryId, reference.Category.Name)).ToArray(),
        skill.Contents.OrderBy(content => content.SortOrder).Select(content => SkillHierarchyReader.Summary(content, true)).ToArray());
}
