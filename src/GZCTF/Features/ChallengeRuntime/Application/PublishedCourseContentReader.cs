using System.Text.Json;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.LearningPaths.Domain;
using GZCTF.Features.SkillTrees.Domain;
using Microsoft.EntityFrameworkCore;
using CanonicalChallenge = GZCTF.Features.ChallengeLibrary.Domain.Challenge;

namespace GZCTF.Features.ChallengeRuntime.Application;

/// <summary>The single effective published content set used by learner access, runtime and scoring.</summary>
public static class PublishedCourseContentReader
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IQueryable<CoursePublishedContent> ActiveContents(AppDbContext db) =>
        db.CoursePublishedContents.AsNoTracking().Where(c =>
            c.Version.SkillTree.DeletedAtUtc == null &&
            c.Version.SkillTree.CurrentPublishedRevisionId == c.VersionId &&
            // A merge rewrites identity globally; ordinary draft publication/enabled edits do not.
            (c.Kind != "challenge" || !db.Challenges.Any(challenge => challenge.Id == c.ContentId &&
                challenge.PublicationState == ChallengePublicationState.Merged)));

    public static async Task<CanonicalChallenge?> GetChallengeAsync(AppDbContext db, Guid id,
        CancellationToken token, bool allowDraft = false, Guid? courseId = null)
    {
        if (allowDraft)
            return await db.Challenges.AsNoTracking().Include(c => c.Localizations).Include(c => c.Flags)
                .Include(c => c.Hints).Include(c => c.Writeups).SingleOrDefaultAsync(c => c.Id == id, token);
        var payload = await ActiveContents(db).Where(c => c.Kind == "challenge" && c.ContentId == id &&
            (courseId == null || c.Version.SkillTreeId == courseId))
            .OrderBy(c => c.VersionId).Select(c => c.PayloadJson).FirstOrDefaultAsync(token);
        if (payload is not null)
        {
            var challenge = ReadChallenge(payload);
            return IsAvailable(challenge) ? challenge : null;
        }
        return null;
    }

    public static async Task<Dictionary<Guid, CanonicalChallenge>> GetChallengesAsync(AppDbContext db,
        CancellationToken token)
    {
        var rows = await ActiveContents(db).Where(c => c.Kind == "challenge")
            .OrderBy(c => c.VersionId).Select(c => new { c.ContentId, c.PayloadJson }).ToArrayAsync(token);
        return rows.GroupBy(c => c.ContentId).Select(g => ReadChallenge(g.First().PayloadJson))
            .Where(IsAvailable).ToDictionary(c => c.Id);
    }

    public static async Task<Lesson?> GetLessonAsync(AppDbContext db, Guid id, CancellationToken token, Guid? courseId = null)
    {
        var payload = await ActiveContents(db).Where(c => c.Kind == "lesson" && c.ContentId == id &&
            (courseId == null || c.Version.SkillTreeId == courseId))
            .OrderBy(c => c.VersionId).Select(c => c.PayloadJson).FirstOrDefaultAsync(token);
        if (payload is null) return null;
        var lesson = ReadLesson(payload);
        return lesson.PublicationState == LessonPublicationState.Published ? lesson : null;
    }

    public static CanonicalChallenge ReadChallenge(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var challenge = root.GetProperty("challenge").Deserialize<CanonicalChallenge>(Options)!;
        challenge.Localizations = root.GetProperty("localizations").Deserialize<List<ChallengeLocalization>>(Options)!;
        challenge.Flags = root.GetProperty("flags").Deserialize<List<ChallengeFlag>>(Options)!;
        challenge.Hints = root.GetProperty("hints").Deserialize<List<ChallengeHint>>(Options)!;
        challenge.Writeups = root.GetProperty("writeups").Deserialize<List<ChallengeWriteup>>(Options)!;
        return challenge;
    }

    public static Lesson ReadLesson(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var lesson = root.GetProperty("lesson").Deserialize<Lesson>(Options)!;
        lesson.Localizations = root.GetProperty("localizations").Deserialize<List<LessonLocalization>>(Options)!;
        return lesson;
    }

    // Store scalar/owned rows only; EF navigation properties contain cycles when authoring drafts.
    public static string FreezeChallenge(CanonicalChallenge challenge) => JsonSerializer.Serialize(new
    {
        challenge = new { challenge.Id, challenge.Type, challenge.CtfCategory, challenge.Difficulty,
            challenge.Score, challenge.PublicationState, challenge.IsEnabled, challenge.ExpectedMinutes,
            challenge.SubmissionLimit, challenge.RuntimeConfigurationJson, challenge.SourceType,
            challenge.SourceId, challenge.SourceName, challenge.SourceMetadataJson },
        localizations = challenge.Localizations.Select(c => new { c.Id, c.ChallengeId, c.Locale, c.Title, c.Summary, c.Body }),
        flags = challenge.Flags.Select(c => new { c.Id, c.ChallengeId, c.Kind, c.Value, c.Template, c.AttachmentPoolKey, c.MetadataJson }),
        hints = challenge.Hints.Select(c => new { c.Id, c.ChallengeId, c.SortOrder, c.Locale, c.Content }),
        writeups = challenge.Writeups.Select(c => new { c.Id, c.ChallengeId, c.Locale, c.Content })
    });

    private static bool IsAvailable(CanonicalChallenge challenge) => challenge.IsEnabled &&
        challenge.PublicationState == ChallengePublicationState.Published;
}
