using System.Text.Json;
using GZCTF.Features.ChallengeLibrary.Application;
using GZCTF.Features.ChallengeRuntime.Application;
using GZCTF.Features.LearningPaths.Domain;
using GZCTF.Features.SkillTrees.Api;
using GZCTF.Features.SkillTrees.Domain;
using Microsoft.EntityFrameworkCore;
using GZCTF.Storage.Interface;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Challenge = GZCTF.Features.ChallengeLibrary.Domain.Challenge;

namespace GZCTF.Features.SkillTrees.Application;

public sealed class CourseWorkspaceContentService(AppDbContext db, IBlobStorage storage, CourseAssetRegistry registry)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<LessonResponse?> GetLessonAsync(Guid treeId, Guid id, string? locale, CancellationToken token)
    {
        var row = await LoadAsync(treeId, id, "lesson", token);
        if (row is null) return null;
        var lesson = PublishedCourseContentReader.ReadLesson(row.PayloadJson);
        lesson.RowVersion = (uint)row.Version;
        return ChallengeLibraryService.ToLessonResponse(lesson, locale, []);
    }
    public async Task<ChallengeEditResponse?> GetChallengeAsync(Guid treeId, Guid id, string? locale, CancellationToken token)
    {
        var row = await LoadAsync(treeId, id, "challenge", token);
        if (row is null) return null;
        var challenge = PublishedCourseContentReader.ReadChallenge(row.PayloadJson);
        challenge.RowVersion = (uint)row.Version;
        return ChallengeLibraryService.ToEditResponse(challenge, locale, []);
    }
    public async Task<Lesson?> GetWorkspaceLessonAsync(Guid treeId, Guid id, CancellationToken token)
    {
        var row = await LoadAsync(treeId, id, "lesson", token);
        return row is null ? null : PublishedCourseContentReader.ReadLesson(row.PayloadJson);
    }
    public async Task<Challenge?> GetWorkspaceChallengeAsync(Guid treeId, Guid id, CancellationToken token)
    {
        var row = await LoadAsync(treeId, id, "challenge", token);
        return row is null ? null : PublishedCourseContentReader.ReadChallenge(row.PayloadJson);
    }
    public async Task<LessonResponse?> UpdateLessonAsync(Guid treeId, Guid id, LessonCommand command, CancellationToken token)
    {
        var row = await LoadAsync(treeId, id, "lesson", token);
        if (row is null) return null;
        RequireVersion(row, command.RowVersion);
        var lesson = PublishedCourseContentReader.ReadLesson(row.PayloadJson);
        ChallengeLibraryService.ApplyLessonCommand(lesson, command, false);
        row.PayloadJson = FreezeLesson(lesson);
        row.Version = checked(row.Version + 1);
        lesson.RowVersion = (uint)row.Version;
        var response = ChallengeLibraryService.ToLessonResponse(lesson, command.Locale, []);
        await SaveAsync(row, response.Title, "", token);
        return response;
    }
    public async Task<ChallengeEditResponse?> UpdateChallengeAsync(Guid treeId, Guid id, ChallengeCommand command, CancellationToken token)
    {
        var row = await LoadAsync(treeId, id, "challenge", token);
        if (row is null) return null;
        RequireVersion(row, command.RowVersion);
        var challenge = PublishedCourseContentReader.ReadChallenge(row.PayloadJson);
        if (command.Type is { } type && type != challenge.Type && challenge.PublicationState != GZCTF.Features.ChallengeLibrary.Domain.ChallengePublicationState.Draft)
            throw new ChallengeTypeImmutableException();
        ChallengeLibraryService.ApplyChallengeCommand(challenge, command, false);
        row.PayloadJson = PublishedCourseContentReader.FreezeChallenge(challenge);
        row.Version = checked(row.Version + 1);
        challenge.RowVersion = (uint)row.Version;
        var response = ChallengeLibraryService.ToEditResponse(challenge, command.Locale, []);
        await SaveAsync(row, response.Challenge.Title, response.Challenge.Summary, token);
        return response;
    }

    public async Task<string> CopyForTransferAsync(Guid sourceTreeId, Guid targetTreeId, Guid id, string kind, CancellationToken token)
    {
        var existing = await db.CourseWorkspaceContents.SingleOrDefaultAsync(c => c.SkillTreeId == targetTreeId && c.Kind == kind && c.ContentId == id, token);
        if (existing is not null) return existing.PayloadJson;
        var source = await LoadAsync(sourceTreeId, id, kind, token) ?? throw new InvalidOperationException("course_transfer_content_missing");
        // Rehome draft asset URLs through the same verified copying path as published assets.
        var payload = source.PayloadJson.Replace($"/api/admin/skill-trees/{sourceTreeId}/workspace/assets/",
            $"/api/course-assets/{sourceTreeId}/{Guid.Empty}/", StringComparison.Ordinal);
        payload = await RehomeAssetsAsync(payload, sourceTreeId, Guid.Empty, targetTreeId, token);
        db.CourseWorkspaceContents.Add(new CourseWorkspaceContent { SkillTreeId = targetTreeId, Kind = kind, ContentId = id,
            Version = 1, PayloadJson = payload });
        return payload;
    }

    public Task LockTransferredAssetsAsync(IEnumerable<string> payloads, CancellationToken token) =>
        registry.LockReferencedAssetsAsync(db, string.Join("\n", payloads), token);

    private static void RequireVersion(CourseWorkspaceContent row, uint? version)
    {
        if (version is null || version != row.Version) throw new SkillTreeRevisionConflictException();
    }
    private async Task SaveAsync(CourseWorkspaceContent row, string title, string summary, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"SkillTreeId\" FROM \"CourseAuthoringWorkspaces\" WHERE \"SkillTreeId\" = {row.SkillTreeId} FOR UPDATE", token);
        await registry.LockReferencedAssetsAsync(db, row.PayloadJson, token);
        var workspace = await db.CourseAuthoringWorkspaces.SingleAsync(w => w.SkillTreeId == row.SkillTreeId, token);
        var document = JsonSerializer.Deserialize<CourseDocument>(workspace.DocumentJson, JsonOptions)!;
        var categories = document.Categories.Select(c => c with { Skills = c.Skills.Select(s => s with {
            Contents = s.Contents.Select(x => x.ContentId == row.ContentId && x.Kind == row.Kind ? x with { Title = title, Summary = summary } : x).ToList()
        }).ToList() }).ToList();
        workspace.DocumentJson = JsonSerializer.Serialize(document with { Categories = categories }, JsonOptions);
        workspace.Version = Guid.NewGuid();
        try { await db.SaveChangesAsync(token); await transaction.CommitAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new SkillTreeRevisionConflictException(); }
    }
    private async Task<CourseWorkspaceContent?> LoadAsync(Guid treeId, Guid id, string kind, CancellationToken token)
    {
        var workspace = await db.CourseAuthoringWorkspaces.Include(w => w.SkillTree)
            .SingleOrDefaultAsync(w => w.SkillTreeId == treeId && w.SkillTree.DeletedAtUtc == null, token);
        if (workspace is null) return null;
        var document = JsonSerializer.Deserialize<CourseDocument>(workspace.DocumentJson, JsonOptions)!;
        if (!document.Categories.SelectMany(c => c.Skills).SelectMany(s => s.Contents).Any(c => c.ContentId == id && c.Kind == kind)) return null;
        var row = await db.CourseWorkspaceContents.SingleOrDefaultAsync(c => c.SkillTreeId == treeId && c.Kind == kind && c.ContentId == id, token);
        if (row is not null) return row;
        var source = await db.CoursePublishedContents.AsNoTracking()
            .Where(c => c.VersionId == workspace.SkillTree.CurrentPublishedRevisionId && c.ContentId == id && c.Kind == kind)
            .Select(c => new { c.Version.SkillTreeId, c.VersionId, c.PayloadJson }).FirstOrDefaultAsync(token);
        // Reuse an effective published version before considering an unpublished library draft.
        source ??= await PublishedCourseContentReader.ActiveContents(db).Where(c => c.Kind == kind && c.ContentId == id)
            .OrderBy(c => c.VersionId).Select(c => new { c.Version.SkillTreeId, c.VersionId, c.PayloadJson }).FirstOrDefaultAsync(token);
        var payload = source is null ? null : await RehomeAssetsAsync(source.PayloadJson, source.SkillTreeId, source.VersionId, treeId, token);
        if (payload is null && kind == "challenge")
        {
            var challenge = await db.Challenges.AsNoTracking().AsSplitQuery().Include(c => c.Localizations).Include(c => c.Flags)
                .Include(c => c.Hints).Include(c => c.Writeups).SingleOrDefaultAsync(c => c.Id == id, token);
            if (challenge is null) return null;
            payload = PublishedCourseContentReader.FreezeChallenge(challenge);
        }
        if (payload is null && kind == "lesson")
        {
            var lesson = await db.Lessons.AsNoTracking().Include(l => l.Localizations).SingleOrDefaultAsync(l => l.Id == id, token);
            if (lesson is null) return null;
            payload = FreezeLesson(lesson);
        }
        row = new CourseWorkspaceContent { SkillTreeId = treeId, Kind = kind, ContentId = id, Version = 1, PayloadJson = payload! };
        db.CourseWorkspaceContents.Add(row);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached;
            row = await db.CourseWorkspaceContents.SingleAsync(c => c.SkillTreeId == treeId && c.Kind == kind && c.ContentId == id, token);
        }
        return row;
    }
    private async Task<string> RehomeAssetsAsync(string payload, Guid sourceTreeId, Guid sourceVersionId, Guid targetTreeId, CancellationToken token)
    {
        var root = JsonNode.Parse(payload)!;
        if (root["localizations"] is not JsonArray localizations) return payload;
        var prefix = $"/api/course-assets/{sourceTreeId}/{sourceVersionId}/";
        var pattern = Regex.Escape(prefix) + "(?<hash>[0-9a-f]{64})/";
        foreach (var localization in localizations.OfType<JsonObject>())
            foreach (var property in localization.ToArray())
            {
                if (!property.Key.Equals("body", StringComparison.OrdinalIgnoreCase) || property.Value is not JsonValue value || !value.TryGetValue<string>(out var body)) continue;
                foreach (Match match in Regex.Matches(body, pattern, RegexOptions.CultureInvariant))
                {
                    var hash = match.Groups["hash"].Value;
                    var sourceKey = CourseWorkspaceAssetsController.AssetKey(sourceTreeId, hash);
                    var targetKey = CourseWorkspaceAssetsController.AssetKey(targetTreeId, hash);
                    // Refresh the grace period even when this target already exists; cleanup
                    // must not race a newly reintroduced reference to an old blob.
                    await using var input = await storage.OpenReadAsync(sourceKey, token);
                    await registry.StoreAsync(targetKey, hash, input, token);
                }
                var contentKind = root["challenge"] is null ? "lesson" : "challenge";
                var scalar = root[contentKind]!.AsObject();
                var idProperty = scalar.First(p => p.Key.Equals("id", StringComparison.OrdinalIgnoreCase));
                var contentId = idProperty.Value!.GetValue<Guid>();
                localization[property.Key] = body.Replace(prefix, $"/api/admin/skill-trees/{targetTreeId}/workspace/assets/", StringComparison.Ordinal)
                    .Replace($"/api/content-assets/{contentKind}/{contentId}/", $"/api/admin/content-assets/{contentKind}/{contentId}/", StringComparison.Ordinal);
            }
        return root.ToJsonString();
    }
    public static string FreezeLesson(Lesson lesson) => JsonSerializer.Serialize(new { lesson = new { lesson.Id, lesson.CreatedAtUtc, lesson.PublicationState },
        localizations = lesson.Localizations.Select(l => new { l.Id, l.LessonId, l.Locale, l.Title, l.Body }) });
}
