using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.ChallengeRuntime.Application;
using GZCTF.Features.LearningPaths.Domain;
using GZCTF.Features.SkillTrees.Api;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Storage.Interface;
using Microsoft.EntityFrameworkCore;
using Challenge = GZCTF.Features.ChallengeLibrary.Domain.Challenge;

namespace GZCTF.Features.SkillTrees.Application;

public sealed record CourseVersionCommand(string Version);
public sealed record CoursePublicationIssue(string Severity, string Code, string Message, Guid? CategoryId = null, Guid? SkillId = null, Guid? ContentId = null);
public sealed record CoursePublicationChanges(IReadOnlyList<string> Added, IReadOnlyList<string> Removed);
public sealed record CourseRuntimeCheck(Guid ContentId, string Title, string? Image, int? Port, int Score);
public sealed record CoursePublicationCheck(string Version, IReadOnlyList<CoursePublicationIssue> Issues, int CategoryCount, int SkillCount, int ChallengeCount, int LessonCount,
    CoursePublicationChanges? Changes = null, IReadOnlyList<string>? Impacts = null, IReadOnlyList<CourseRuntimeCheck>? RuntimeChecks = null, string? PublishedVersion = null);

public sealed class CoursePublicationService(AppDbContext db, CourseWorkspaceContentService contents,
    IBlobStorage storage, CourseAssetRegistry assetRegistry)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record Prepared(CoursePublicationCheck Check, CourseDocument Document, Dictionary<Guid, Challenge> Challenges, Dictionary<Guid, Lesson> Lessons);

    public async Task<CoursePublicationCheck?> CheckAsync(Guid treeId, string version, CancellationToken token) =>
        (await PrepareAsync(treeId, version, token))?.Check;

    public async Task<SkillTreeDetailResponse?> PreviewAsync(Guid treeId, CancellationToken token)
    {
        var version = await db.CourseAuthoringWorkspaces.AsNoTracking().Where(w => w.SkillTreeId == treeId)
            .Select(w => (Guid?)w.Version).SingleOrDefaultAsync(token);
        if (version is null) return null;
        var prepared = await PrepareAsync(treeId, version.Value.ToString(), token);
        return prepared is null ? null : SkillTreeQueryService.ToDetail(treeId, ToPublicDocument(prepared));
    }

    private async Task<Prepared?> PrepareAsync(Guid treeId, string version, CancellationToken token)
    {
        var workspace = await db.CourseAuthoringWorkspaces.AsNoTracking().Include(w => w.SkillTree)
            .SingleOrDefaultAsync(w => w.SkillTreeId == treeId && w.SkillTree.DeletedAtUtc == null, token);
        if (workspace is null) return null;
        if (!Guid.TryParse(version, out var parsed) || parsed != workspace.Version) throw new SkillTreeRevisionConflictException();
        var doc = JsonSerializer.Deserialize<CourseDocument>(workspace.DocumentJson, JsonOptions)!;
        var issues = new List<CoursePublicationIssue>();
        var categoryIds = doc.Categories.Select(c => c.CategoryId).Distinct().ToArray();
        var skillIds = doc.Categories.SelectMany(c => c.Skills).Select(s => s.SkillId).Distinct().ToArray();
        var activeCategories = await db.SkillCategories.AsNoTracking().Where(c => categoryIds.Contains(c.Id) && c.DeletedAtUtc == null).Select(c => c.Id).ToArrayAsync(token);
        var activeSkills = await db.Skills.AsNoTracking().Where(s => skillIds.Contains(s.Id) && s.DeletedAtUtc == null).Select(s => s.Id).ToArrayAsync(token);
        foreach (var missing in categoryIds.Except(activeCategories)) issues.Add(new("error", "category_missing", "A category is no longer available.", CategoryId: missing));
        foreach (var missing in skillIds.Except(activeSkills)) issues.Add(new("error", "skill_missing", "A skill is no longer available.", SkillId: missing));
        if (doc.Categories.Count == 0) issues.Add(new("warning", "empty_course", "The course has no categories."));
        if (string.IsNullOrWhiteSpace(doc.Summary)) issues.Add(new("warning", "missing_summary", "The course has no summary."));
        if (string.IsNullOrWhiteSpace(doc.CoverUrl)) issues.Add(new("warning", "missing_cover", "The course has no cover."));
        if (workspace.CoverStorageKey is { } coverKey)
        {
            try { await using var cover = await storage.OpenReadAsync(coverKey, token); _ = await cover.ReadAsync(new byte[1], token); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
            { issues.Add(new("error", "cover_unreadable", "The course cover cannot be read from storage.")); }
        }
        foreach (var category in doc.Categories)
        {
            if (category.Skills.Count == 0) issues.Add(new("warning", "empty_category", "This category has no skills.", category.CategoryId));
            foreach (var skill in category.Skills)
            {
                if (!skill.IsEnabled) issues.Add(new("warning", "disabled_skill", "This skill is disabled.", category.CategoryId, skill.SkillId));
                else if (skill.Contents.Count == 0) issues.Add(new("warning", "empty_skill", "This skill has no content.", category.CategoryId, skill.SkillId));
            }
        }
        var items = doc.Categories.SelectMany(c => c.Skills.Where(s => s.IsEnabled)).SelectMany(s => s.Contents)
            .DistinctBy(c => (c.Kind, c.ContentId)).ToArray();
        var challenges = new Dictionary<Guid, Challenge>(); var lessons = new Dictionary<Guid, Lesson>();
        foreach (var item in items)
        {
            if (item.Kind == "lesson")
            {
                var canonicalState = await db.Lessons.AsNoTracking().Where(l => l.Id == item.ContentId).Select(l => (LessonPublicationState?)l.PublicationState).SingleOrDefaultAsync(token);
                if (canonicalState is null or LessonPublicationState.Retired)
                { issues.Add(new("error", "lesson_identity_unavailable", "The lesson identity is missing or retired.", ContentId: item.ContentId)); continue; }
                var lesson = await contents.GetWorkspaceLessonAsync(treeId, item.ContentId, token);
                if (lesson is null || lesson.PublicationState == LessonPublicationState.Retired || !lesson.Localizations.Any(l => !string.IsNullOrWhiteSpace(l.Title)))
                    issues.Add(new("error", "lesson_invalid", "The lesson is missing, retired, or has no title.", ContentId: item.ContentId));
                else
                {
                    lessons.Add(item.ContentId, lesson);
                    await ValidateBodyAssetsAsync(treeId, "lesson", item.ContentId, lesson.Localizations.Select(l => l.Body), issues, token);
                }
            }
            else
            {
                var canonicalState = await db.Challenges.AsNoTracking().Where(c => c.Id == item.ContentId).Select(c => (ChallengePublicationState?)c.PublicationState).SingleOrDefaultAsync(token);
                if (canonicalState is null or ChallengePublicationState.Retired or ChallengePublicationState.Merged)
                { issues.Add(new("error", "challenge_identity_unavailable", "The challenge identity is missing, retired, or merged.", ContentId: item.ContentId)); continue; }
                var challenge = await contents.GetWorkspaceChallengeAsync(treeId, item.ContentId, token);
                if (challenge is null || challenge.PublicationState is ChallengePublicationState.Retired or ChallengePublicationState.Merged ||
                    !challenge.Localizations.Any(l => !string.IsNullOrWhiteSpace(l.Title)) || challenge.Localizations.Any(l => string.IsNullOrWhiteSpace(l.Body)))
                { issues.Add(new("error", "challenge_invalid", "The challenge is missing, retired, or needs a title and description.", ContentId: item.ContentId)); continue; }
                if (!challenge.IsEnabled) { issues.Add(new("warning", "disabled_challenge", "This challenge is disabled.", ContentId: item.ContentId)); continue; }
                await ValidateBodyAssetsAsync(treeId, "challenge", item.ContentId, challenge.Localizations.Select(l => l.Body), issues, token);
                try { await ChallengePublicationValidator.ValidateAsync(challenge, storage, token); }
                catch (ContentPublicationValidationException ex) { issues.Add(new("error", ex.Code, ex.Message, ContentId: item.ContentId)); }
                var others = await PublishedCourseContentReader.ActiveContents(db).Where(c => c.Kind == "challenge" && c.ContentId == item.ContentId && c.Version.SkillTreeId != treeId)
                    .Select(c => new { c.Version.SkillTreeId, c.PayloadJson }).ToArrayAsync(token);
                var fingerprint = RuntimeFingerprint(challenge);
                var affected = others.Where(c => RuntimeFingerprint(PublishedCourseContentReader.ReadChallenge(c.PayloadJson)) != fingerprint)
                    .Select(c => c.SkillTreeId).Distinct().ToArray();
                if (affected.Length != 0) issues.Add(new("error", "shared_challenge_conflict", $"Challenge runtime or scoring conflicts with published courses: {string.Join(", ", affected)}.", ContentId: item.ContentId));
                challenges.Add(item.ContentId, challenge);
            }
        }
        var oldJson = await db.CoursePublishedVersions.AsNoTracking().Where(v => v.Id == workspace.SkillTree.CurrentPublishedRevisionId)
            .Select(v => v.PublicDocumentJson).SingleOrDefaultAsync(token);
        var oldDoc = oldJson is null ? null : JsonSerializer.Deserialize<CourseDocument>(oldJson, JsonOptions);
        var provisional = new Prepared(new(version, issues, 0, 0, 0, 0), doc, challenges, lessons);
        var previous = Outline(oldDoc); var next = Outline(ToPublicDocument(provisional));
        var changes = new CoursePublicationChanges(next.Where(p => !previous.ContainsKey(p.Key)).Select(p => p.Value).ToArray(),
            previous.Where(p => !next.ContainsKey(p.Key)).Select(p => p.Value).ToArray());
        var impacts = new List<string>();
        foreach (var challenge in challenges.Values)
        {
            var oldPayload = await db.CoursePublishedContents.AsNoTracking().Where(c => c.VersionId == workspace.SkillTree.CurrentPublishedRevisionId && c.Kind == "challenge" && c.ContentId == challenge.Id)
                .Select(c => c.PayloadJson).SingleOrDefaultAsync(token);
            if (oldPayload is null) continue;
            var oldChallenge = PublishedCourseContentReader.ReadChallenge(oldPayload);
            if (oldChallenge.Score != challenge.Score) impacts.Add($"Challenge {challenge.Id}: score {oldChallenge.Score} → {challenge.Score}; learner totals may change.");
            if (RuntimeFingerprint(oldChallenge) != RuntimeFingerprint(challenge)) impacts.Add($"Challenge {challenge.Id}: runtime or submission rules change.");
        }
        foreach (var removed in previous.Keys.Except(next.Keys).Where(k => k.StartsWith("challenge:", StringComparison.Ordinal)))
            impacts.Add($"Removed {previous[removed]}; learner totals may change if this was its last published reference.");
        var runtimeChecks = challenges.Values.Select(RuntimeCheck).ToArray();
        return new(new(version, issues, doc.Categories.Count, doc.Categories.SelectMany(c => c.Skills).Where(s => s.IsEnabled).Select(s => s.SkillId).Distinct().Count(),
            challenges.Count, lessons.Count, changes, impacts, runtimeChecks), doc, challenges, lessons);
    }
    internal async Task ValidateBodyAssetsAsync(Guid treeId, string kind, Guid contentId, IEnumerable<string> bodies,
        List<CoursePublicationIssue> issues, CancellationToken token)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var invalid = false;
        foreach (var body in bodies)
        {
            foreach (Match match in Regex.Matches(body, @"/api/admin/skill-trees/(?<tree>[0-9a-fA-F-]{36})/workspace/assets/(?<hash>[0-9a-f]{64})/", RegexOptions.CultureInvariant))
            {
                if (!Guid.TryParse(match.Groups["tree"].Value, out var referencedTree) || referencedTree != treeId) { invalid = true; continue; }
                keys.Add(CourseWorkspaceAssetsController.AssetKey(treeId, match.Groups["hash"].Value));
            }
            foreach (Match match in Regex.Matches(body, @"/api/admin/content-assets/(?<kind>lesson|challenge)/(?<id>[0-9a-fA-F-]{36})/(?<hash>[0-9a-f]{64})/", RegexOptions.CultureInvariant))
            {
                if (match.Groups["kind"].Value != kind || !Guid.TryParse(match.Groups["id"].Value, out var referencedId) || referencedId != contentId) { invalid = true; continue; }
                keys.Add(ContentAssetsController.AssetKey(kind, contentId, match.Groups["hash"].Value));
            }
            foreach (Match match in Regex.Matches(body, @"/api/course-assets/(?<tree>[0-9a-fA-F-]{36})/(?<version>[0-9a-fA-F-]{36})/(?<hash>[0-9a-f]{64})/", RegexOptions.CultureInvariant))
            {
                if (!Guid.TryParse(match.Groups["tree"].Value, out var referencedTree) || referencedTree != treeId) { invalid = true; continue; }
                keys.Add(CourseWorkspaceAssetsController.AssetKey(treeId, match.Groups["hash"].Value));
            }
            foreach (Match match in Regex.Matches(body, @"/api/content-assets/(?<kind>lesson|challenge)/(?<id>[0-9a-fA-F-]{36})/(?<hash>[0-9a-f]{64})/", RegexOptions.CultureInvariant))
            {
                if (match.Groups["kind"].Value != kind || !Guid.TryParse(match.Groups["id"].Value, out var referencedId) || referencedId != contentId) { invalid = true; continue; }
                keys.Add(ContentAssetsController.AssetKey(kind, contentId, match.Groups["hash"].Value));
            }
            foreach (Match match in Regex.Matches(body, @"/Assets/(?<hash>[0-9a-f]{64})/", RegexOptions.CultureInvariant))
            {
                var hash = match.Groups["hash"].Value;
                keys.Add(StoragePath.Combine(PathHelper.Uploads, hash[..2], hash[2..4], hash));
            }
        }
        foreach (var key in keys)
        {
            try { await using var stream = await storage.OpenReadAsync(key, token); _ = await stream.ReadAsync(new byte[1], token); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) { invalid = true; }
        }
        if (invalid) issues.Add(new("error", "content_asset_unreadable", "A referenced local content file is missing or unreadable.", ContentId: contentId));
    }

    private static Dictionary<string, string> Outline(CourseDocument? doc)
    {
        var result = new Dictionary<string, string>();
        if (doc is null) return result;
        foreach (var category in doc.Categories)
        {
            result[$"category:{category.CategoryId}"] = category.Name;
            foreach (var skill in category.Skills.Where(s => s.IsEnabled))
            {
                result[$"skill:{category.CategoryId}:{skill.SkillId}"] = $"{category.Name} / {skill.Name}";
                foreach (var content in skill.Contents) result[$"{content.Kind}:{category.CategoryId}:{skill.SkillId}:{content.ContentId}"] = $"{category.Name} / {skill.Name} / {content.Title}";
            }
        }
        return result;
    }
    private static CourseRuntimeCheck RuntimeCheck(Challenge c)
    {
        string? image = null; int? port = null;
        try
        {
            if (c.RuntimeConfigurationJson is { } json)
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if ((property.Name.Equals("image", StringComparison.OrdinalIgnoreCase) || property.Name.Equals("containerImage", StringComparison.OrdinalIgnoreCase)) && property.Value.ValueKind == JsonValueKind.String) image ??= property.Value.GetString();
                        if (property.Name.Equals("exposedPort", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var value)) port = value;
                    }
            }
        }
        catch (JsonException) { }
        var title = c.Localizations.OrderBy(l => l.Locale == "en" ? 0 : l.Locale == "zh-CN" ? 1 : 2).FirstOrDefault()?.Title ?? "";
        return new(c.Id, title, image, port, c.Score);
    }
    private static CourseDocument ToPublicDocument(Prepared prepared) => prepared.Document with {
        Categories = prepared.Document.Categories.Select(c => c with { Skills = c.Skills.Where(s => s.IsEnabled).Select(s => s with {
            Contents = s.Contents.Where(x => x.Kind == "lesson" ? prepared.Lessons.ContainsKey(x.ContentId) : prepared.Challenges.ContainsKey(x.ContentId))
                .Select(x => PublicContent(x, prepared)).ToList()
        }).ToList() }).ToList()
    };
    private static CourseContent PublicContent(CourseContent item, Prepared prepared)
    {
        if (item.Kind == "lesson")
        {
            var lesson = prepared.Lessons[item.ContentId];
            var text = lesson.Localizations.FirstOrDefault(l => l.Title == item.Title)
                ?? lesson.Localizations.OrderBy(l => l.Locale == "en" ? 0 : l.Locale == "zh-CN" ? 1 : 2).FirstOrDefault();
            return item with { Title = text?.Title ?? "", Summary = "", State = null, ExpectedMinutes = 0, Difficulty = "" };
        }
        var challenge = prepared.Challenges[item.ContentId];
        var localization = challenge.Localizations.FirstOrDefault(l => l.Title == item.Title)
            ?? challenge.Localizations.OrderBy(l => l.Locale == "en" ? 0 : l.Locale == "zh-CN" ? 1 : 2).FirstOrDefault();
        return item with { Title = localization?.Title ?? "", Summary = localization?.Summary ?? "", State = null,
            ExpectedMinutes = challenge.ExpectedMinutes, Difficulty = challenge.Difficulty.ToString() };
    }

    internal static string RewriteAssetUrls(string payload, Guid treeId, Guid versionId, string kind, Guid contentId)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(payload)!;
        var from = $"/api/admin/skill-trees/{treeId}/workspace/assets/";
        var to = $"/api/course-assets/{treeId}/{versionId}/";
        if (root["localizations"] is System.Text.Json.Nodes.JsonArray localizations)
            foreach (var localization in localizations.OfType<System.Text.Json.Nodes.JsonObject>())
                foreach (var property in localization.ToArray())
                    if (property.Key.Equals("body", StringComparison.OrdinalIgnoreCase) && property.Value is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var body))
                        localization[property.Key] = body.Replace(from, to, StringComparison.Ordinal)
                            .Replace($"/api/admin/content-assets/{kind}/{contentId}/", $"/api/content-assets/{kind}/{contentId}/", StringComparison.Ordinal);
        return root.ToJsonString();
    }

    internal static string RuntimeFingerprint(Challenge c) => JsonSerializer.Serialize(new {
        c.Type, c.Score, c.SubmissionLimit, c.SourceType, c.SourceId,
        RuntimeConfigurationJson = NormalizeJson(c.RuntimeConfigurationJson),
        Flags = c.Flags.Select(f => JsonSerializer.Serialize(new { f.Kind, f.Value, f.Template, f.AttachmentPoolKey, MetadataJson = NormalizeJson(f.MetadataJson) }))
            .OrderBy(value => value, StringComparer.Ordinal).ToArray()
    });
    private static string? NormalizeJson(string? json)
    {
        if (json is null) return null;
        try
        {
            using var document = JsonDocument.Parse(json); using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, document.RootElement);
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException) { return json; }
    }
    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value); }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        { writer.WriteStartArray(); foreach (var value in element.EnumerateArray()) WriteCanonical(writer, value); writer.WriteEndArray(); }
        else element.WriteTo(writer);
    }

    public async Task<CoursePublicationCheck?> PublishAsync(Guid treeId, string version, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"SkillTreeId\" FROM \"CourseAuthoringWorkspaces\" WHERE \"SkillTreeId\" = {treeId} FOR UPDATE", token);
        var prepared = await PrepareAsync(treeId, version, token);
        if (prepared is null) return null;
        if (prepared.Check.Issues.Any(i => i.Severity == "error")) return prepared.Check;
        var assetReferences = string.Join("\n", prepared.Challenges.Values.Select(PublishedCourseContentReader.FreezeChallenge)
            .Concat(prepared.Lessons.Values.Select(CourseWorkspaceContentService.FreezeLesson)));
        try { await assetRegistry.LockReferencedAssetsAsync(db, assetReferences, token); }
        catch (CourseAssetUnavailableException)
        {
            return prepared.Check with { Issues = prepared.Check.Issues.Append(new CoursePublicationIssue("error", "content_asset_unreadable", "A referenced private file is missing.")).ToArray() };
        }
        var tree = await db.SkillTrees.Include(t => t.Revisions).SingleAsync(t => t.Id == treeId, token);
        var old = tree.Revisions.FirstOrDefault(r => r.Status == SkillTreeRevisionStatus.Published);
        if (old is not null) old.Status = SkillTreeRevisionStatus.Archived;
        var draft = tree.Revisions.FirstOrDefault(r => r.Status == SkillTreeRevisionStatus.Draft);
        if (draft is null) { draft = new SkillTreeRevision { SkillTreeId = treeId }; db.SkillTreeRevisions.Add(draft); }
        else await db.SkillTreeCategoryRefs.Where(r => r.RevisionId == draft.Id).ExecuteDeleteAsync(token);
        draft.Status = SkillTreeRevisionStatus.Published; draft.PublishedAtUtc = DateTimeOffset.UtcNow;
        for (var i = 0; i < prepared.Document.Categories.Count; i++)
            db.SkillTreeCategoryRefs.Add(new SkillTreeCategoryRef { RevisionId = draft.Id, CategoryId = prepared.Document.Categories[i].CategoryId, SortOrder = i });
        var workspace = await db.CourseAuthoringWorkspaces.SingleAsync(w => w.SkillTreeId == treeId, token);
        var publicDoc = ToPublicDocument(prepared) with { CoverUrl = workspace.CoverStorageKey is null ? prepared.Document.CoverUrl : $"/api/skill-trees/{treeId}/cover/{draft.Id}" };
        var published = new CoursePublishedVersion { Id = draft.Id, SkillTreeId = treeId, PublishedAtUtc = draft.PublishedAtUtc.Value,
            PublicDocumentJson = JsonSerializer.Serialize(publicDoc, JsonOptions), CoverStorageKey = workspace.CoverStorageKey };
        foreach (var challenge in prepared.Challenges.Values)
        {
            challenge.PublicationState = ChallengePublicationState.Published;
            published.Contents.Add(new CoursePublishedContent { Id = Guid.CreateVersion7(), VersionId = draft.Id, ContentId = challenge.Id,
                Kind = "challenge", PayloadJson = RewriteAssetUrls(PublishedCourseContentReader.FreezeChallenge(challenge), treeId, draft.Id, "challenge", challenge.Id) });
        }
        foreach (var lesson in prepared.Lessons.Values)
        {
            lesson.PublicationState = LessonPublicationState.Published;
            published.Contents.Add(new CoursePublishedContent { Id = Guid.CreateVersion7(), VersionId = draft.Id, ContentId = lesson.Id,
                Kind = "lesson", PayloadJson = RewriteAssetUrls(CourseWorkspaceContentService.FreezeLesson(lesson), treeId, draft.Id, "lesson", lesson.Id) });
        }
        db.CoursePublishedVersions.Add(published);
        tree.CurrentPublishedRevisionId = draft.Id;
        workspace.Version = Guid.NewGuid();
        try { await db.SaveChangesAsync(token); await tx.CommitAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new SkillTreeRevisionConflictException(); }
        return prepared.Check with { PublishedVersion = draft.Id.ToString() };
    }
}
