using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.LearningPaths.Domain;
using GZCTF.Features.SkillTrees.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.SkillTrees.Application;

/// <summary>All live reads originate at SkillContents. CategoryContents is retained only as migration history.</summary>
public static class SkillHierarchyReader
{
    public static IQueryable<CategoryContent> CategoryContents(AppDbContext db, IEnumerable<Guid> categoryIds) =>
        db.CategorySkillRefs.AsNoTracking()
            .Where(reference => categoryIds.Contains(reference.CategoryId) && reference.Category.DeletedAtUtc == null &&
                reference.Skill.DeletedAtUtc == null && reference.Skill.IsEnabled)
            .SelectMany(reference => reference.Skill.Contents.Select(content => new CategoryContent
            {
                Id = content.Id, CategoryId = reference.CategoryId, SortOrder = content.SortOrder,
                ChallengeId = content.ChallengeId, LessonId = content.LessonId, CreatedAtUtc = content.CreatedAtUtc
            }));

    public static async Task<Dictionary<Guid, IReadOnlyList<SkillPublicResponse>>> LoadSkillsAsync(
        AppDbContext db, IReadOnlyList<Guid> categoryIds, bool publicOnly, CancellationToken token)
    {
        var references = await db.CategorySkillRefs.AsNoTracking().AsSplitQuery()
            .Include(reference => reference.Skill).ThenInclude(skill => skill.Contents)
                .ThenInclude(content => content.Challenge!).ThenInclude(challenge => challenge.Localizations)
            .Include(reference => reference.Skill).ThenInclude(skill => skill.Contents)
                .ThenInclude(content => content.Lesson!).ThenInclude(lesson => lesson.Localizations)
            .Where(reference => categoryIds.Contains(reference.CategoryId) && reference.Skill.DeletedAtUtc == null &&
                (!publicOnly || reference.Skill.IsEnabled))
            .OrderBy(reference => reference.SortOrder).ToListAsync(token);
        return references.GroupBy(reference => reference.CategoryId).ToDictionary(group => group.Key,
            group => (IReadOnlyList<SkillPublicResponse>)group.Select(reference =>
            {
                var contents = reference.Skill.Contents.OrderBy(content => content.SortOrder)
                    .Where(content => !publicOnly || IsPublished(content)).Select(content => Summary(content, !publicOnly)).ToArray();
                return new SkillPublicResponse(reference.SkillId, reference.Skill.Name, reference.Skill.Summary,
                    reference.Skill.IconKey, reference.SortOrder, contents.Count(content => content.Kind == "challenge"),
                    contents.Count(content => content.Kind == "lesson"), contents, reference.Skill.IsEnabled);
            }).ToArray());
    }

    public static IReadOnlyList<SkillTreeContentSummaryResponse> Flatten(IReadOnlyList<SkillPublicResponse> skills) =>
        skills.OrderBy(skill => skill.SortOrder).SelectMany(skill => skill.Contents)
            .DistinctBy(content => (content.Kind, content.ContentId)).Select((content, index) => content with { SortOrder = index }).ToArray();

    public static bool IsPublished(SkillContent content) =>
        content.Challenge is { PublicationState: ChallengePublicationState.Published, IsEnabled: true } ||
        content.Lesson is { PublicationState: LessonPublicationState.Published };

    public static SkillTreeContentSummaryResponse Summary(SkillContent content, bool includeState)
    {
        if (content.Challenge is { } challenge)
        {
            var text = challenge.Localizations.OrderBy(item => item.Locale == "en" ? 0 : item.Locale == "zh-CN" ? 1 : 2).FirstOrDefault();
            return new(challenge.Id, "challenge", content.SortOrder, text?.Title ?? "", text?.Summary ?? "",
                challenge.ExpectedMinutes, challenge.Difficulty.ToString(), includeState ? (challenge.PublicationState == ChallengePublicationState.Published && !challenge.IsEnabled ? "Disabled" : challenge.PublicationState.ToString()) : null);
        }
        var lesson = content.Lesson!;
        return new(lesson.Id, "lesson", content.SortOrder,
            lesson.Localizations.OrderBy(item => item.Locale == "en" ? 0 : item.Locale == "zh-CN" ? 1 : 2).FirstOrDefault()?.Title ?? "",
            "", 0, "", includeState ? lesson.PublicationState.ToString() : null);
    }

    public static string? CoverUrl(string? hash) => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigitLower)
        ? $"/Assets/{hash}/skill-tree-cover.webp" : null;
}
