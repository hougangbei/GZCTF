using System.Text.Json;
using GZCTF.Features.Imports.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.Imports.Application;

public sealed record PlatformHistoryBatchInfo(Guid Id, string SourceInstance, string Format, string Title,
    DateTimeOffset CreatedAtUtc);
public sealed record PlatformHistorySubmissionInfo(Guid Id, Guid BatchId, string SourceId, string? SourceUserName,
    string? SourceTeamName, string SourceChallengeId, string ChallengeTitle, string Result, int? Score,
    DateTimeOffset? SubmittedAtUtc, string Attribution);
public sealed record PlatformHistoryAwardInfo(Guid Id, Guid BatchId, string SourceId, string? SourceUserName,
    string? SourceTeamName, string Name, int Value, DateTimeOffset? AwardedAtUtc, string Attribution);
public sealed record PlatformHistoryMemberInfo(Guid Id, Guid BatchId, string SourceUserId, string SourceUserName,
    string? SourceTeamId, string? SourceTeamName);
public sealed record PlatformHistoryPage(IReadOnlyList<PlatformHistoryBatchInfo> Batches,
    IReadOnlyList<PlatformHistorySubmissionInfo> Submissions, IReadOnlyList<PlatformHistoryAwardInfo> Awards,
    int SubmissionCount, int AwardCount, int Skip, int Take,
    IReadOnlyList<PlatformHistoryMemberInfo>? Members = null, int MemberCount = 0);

public sealed class PlatformHistoryService(AppDbContext db, IDataProtectionProvider protection)
{
    private readonly IDataProtector protector = protection.CreateProtector("GZCTF.PlatformImport.History.v1");

    public async Task ImportAsync(PlatformImportPackage package, PlatformImportOptions options,
        PlatformImportBatch batch, CancellationToken cancellationToken = default)
    {
        // Authentication material is discarded before encryption. History disabled means no source history snapshot.
        batch.ProtectedMetadata = options.IncludeHistory
            ? protector.Protect(JsonSerializer.Serialize(new
            {
                Source = SanitizeMetadata(package.MetadataJson),
                Teams = package.Teams.Select(t => new { t.SourceId, t.Name, Metadata = SanitizeMetadata(t.MetadataJson) })
            }))
            : protector.Protect(JsonSerializer.Serialize(new { package.Format, package.Title, package.Fingerprint }));
        if (!options.IncludeHistory)
            return;
        var maps = await db.PlatformImportMaps.Where(m => m.SourceInstance == options.SourceInstance && m.Kind == "user")
            .ToDictionaryAsync(m => m.SourceId, m => m.TargetId, cancellationToken);
        // Include mappings added in the ambient transaction before a save as well.
        foreach (var map in db.PlatformImportMaps.Local.Where(m => m.SourceInstance == options.SourceInstance && m.Kind == "user"))
            maps[map.SourceId] = map.TargetId;
        var existingUserIds = await db.Users.Where(u => maps.Values.Contains(u.Id)).Select(u => u.Id).ToHashSetAsync(cancellationToken);
        var skippedIds = new HashSet<string>(options.SkipUserIds ?? [], StringComparer.Ordinal);
        var users = package.Users.ToDictionary(u => u.SourceId, StringComparer.Ordinal);
        var teams = package.Teams.ToDictionary(t => t.SourceId, StringComparer.Ordinal);
        var challenges = package.Challenges.ToDictionary(c => c.SourceId, StringComparer.Ordinal);
        foreach (var user in package.Users)
        {
            teams.TryGetValue(user.TeamId ?? string.Empty, out var team);
            db.PlatformHistoryMembers.Add(new PlatformHistoryMember
            {
                BatchId = batch.Id,
                Batch = batch,
                SourceUserId = user.SourceId,
                SourceUserName = user.UserName,
                SourceTeamId = team?.SourceId,
                SourceTeamName = team?.Name,
                UserId = !skippedIds.Contains(user.SourceId) && maps.TryGetValue(user.SourceId, out var userId) &&
                    existingUserIds.Contains(userId) ? userId : null,
                // Account retention is an explicit allowlist. PasswordHash is only consumed by the login migration.
                ProtectedMetadata = protector.Protect(JsonSerializer.Serialize(new
                {
                    user.SourceId,
                    user.UserName,
                    user.Email,
                    user.Verified,
                    user.Banned,
                    user.TeamId,
                    user.CreatedAtUtc,
                    Profile = AccountProfileMetadata(user.MetadataJson)
                }))
            });
        }
        foreach (var submission in package.Submissions)
        {
            users.TryGetValue(submission.UserId ?? string.Empty, out var user);
            teams.TryGetValue(submission.TeamId ?? string.Empty, out var team);
            challenges.TryGetValue(submission.ChallengeId, out var challenge);
            db.PlatformHistorySubmissions.Add(new PlatformHistorySubmission
            {
                BatchId = batch.Id,
                Batch = batch,
                SourceId = submission.SourceId,
                SourceUserId = submission.UserId,
                SourceUserName = user?.UserName,
                SourceTeamId = submission.TeamId,
                SourceTeamName = team?.Name,
                SourceChallengeId = submission.ChallengeId,
                ChallengeTitle = challenge?.Title ?? submission.ChallengeId,
                Result = SafeResult(submission.Result),
                SubmittedAtUtc = submission.SubmittedAtUtc,
                Score = submission.Score,
                ProtectedMetadata = protector.Protect(JsonSerializer.Serialize(new
                {
                    submission.SourceId,
                    submission.UserId,
                    submission.TeamId,
                    submission.ChallengeId,
                    submission.Result,
                    submission.SubmittedAtUtc,
                    submission.Score,
                    Metadata = SanitizeMetadata(submission.MetadataJson)
                }))
            });
        }
        foreach (var award in package.Awards)
        {
            users.TryGetValue(award.UserId ?? string.Empty, out var user);
            teams.TryGetValue(award.TeamId ?? string.Empty, out var team);
            db.PlatformHistoryAwards.Add(new PlatformHistoryAward
            {
                BatchId = batch.Id,
                Batch = batch,
                SourceId = award.SourceId,
                SourceUserId = award.UserId,
                SourceUserName = user?.UserName,
                SourceTeamId = award.TeamId,
                SourceTeamName = team?.Name,
                Name = award.Name,
                Value = award.Value,
                AwardedAtUtc = award.AwardedAtUtc,
                ProtectedMetadata = protector.Protect(JsonSerializer.Serialize(new
                {
                    award.SourceId,
                    award.UserId,
                    award.TeamId,
                    award.Name,
                    award.Value,
                    award.AwardedAtUtc,
                    Metadata = SanitizeMetadata(award.MetadataJson)
                }))
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    // This boolean is supplied by the authenticated server controller, never from request query data.
    public async Task<PlatformHistoryPage> GetAsync(Guid userId, bool administrator, int skip = 0, int take = 100,
        CancellationToken cancellationToken = default)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);
        var members = db.PlatformHistoryMembers.AsNoTracking().Where(m => m.UserId == userId);
        var submissions = db.PlatformHistorySubmissions.AsNoTracking();
        var awards = db.PlatformHistoryAwards.AsNoTracking();
        if (!administrator)
        {
            submissions = submissions.Where(s => members.Any(m => m.BatchId == s.BatchId &&
                (m.SourceUserId == s.SourceUserId ||
                 (s.SourceTeamId != null && m.SourceTeamId == s.SourceTeamId))));
            awards = awards.Where(a => members.Any(m => m.BatchId == a.BatchId &&
                (m.SourceUserId == a.SourceUserId ||
                 (a.SourceTeamId != null && m.SourceTeamId == a.SourceTeamId))));
        }
        var visibleMembers = administrator ? db.PlatformHistoryMembers.AsNoTracking() : members;
        var memberCount = await visibleMembers.CountAsync(cancellationToken);
        var memberItems = await visibleMembers.OrderBy(m => m.SourceUserName).ThenBy(m => m.Id)
            .Skip(skip).Take(take).Select(m => new PlatformHistoryMemberInfo(m.Id, m.BatchId, m.SourceUserId,
                m.SourceUserName, m.SourceTeamId, m.SourceTeamName)).ToListAsync(cancellationToken);
        var submissionCount = await submissions.CountAsync(cancellationToken);
        var awardCount = await awards.CountAsync(cancellationToken);
        // Always explicitly project; source metadata may contain submitted flags and credentials.
        var submissionItems = await submissions.OrderByDescending(s => s.SubmittedAtUtc).ThenBy(s => s.Id)
            .Skip(skip).Take(take).Select(s => new PlatformHistorySubmissionInfo(s.Id, s.BatchId, s.SourceId,
                s.SourceUserName, s.SourceTeamName, s.SourceChallengeId, s.ChallengeTitle, s.Result, s.Score,
                s.SubmittedAtUtc, s.SourceTeamId != null ? "team" : "personal")).ToListAsync(cancellationToken);
        var awardItems = await awards.OrderByDescending(a => a.AwardedAtUtc).ThenBy(a => a.Id)
            .Skip(skip).Take(take).Select(a => new PlatformHistoryAwardInfo(a.Id, a.BatchId, a.SourceId,
                a.SourceUserName, a.SourceTeamName, a.Name, a.Value, a.AwardedAtUtc,
                a.SourceTeamId != null ? "team" : "personal")).ToListAsync(cancellationToken);
        var batchIds = submissionItems.Select(s => s.BatchId).Concat(awardItems.Select(a => a.BatchId))
            .Concat(memberItems.Select(m => m.BatchId)).Distinct().ToArray();
        var batchItems = await db.PlatformImportBatches.AsNoTracking().Where(b => batchIds.Contains(b.Id))
            .OrderByDescending(b => b.CreatedAtUtc).Select(b => new PlatformHistoryBatchInfo(b.Id, b.SourceInstance,
                b.Format, b.Title, b.CreatedAtUtc)).ToListAsync(cancellationToken);
        return new(batchItems, submissionItems, awardItems, submissionCount, awardCount, skip, take, memberItems, memberCount);
    }

    private static JsonElement AccountProfileMetadata(string json)
    {
        var safe = SanitizeMetadata(json);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "bio", "country", "affiliation", "website", "language", "hidden", "type" };
        var profile = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (safe.ValueKind == JsonValueKind.Object)
            foreach (var property in safe.EnumerateObject())
                if (allowed.Contains(property.Name))
                    profile[property.Name] = property.Value;
        return JsonSerializer.SerializeToElement(profile);
    }

    // Defense in depth for adapters: retain inspectable source/plugin JSON, never opaque auth blobs.
    internal static JsonElement SanitizeMetadata(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                return JsonSerializer.SerializeToElement(new { });
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
                WriteSafeMetadata(writer, document.RootElement, 0);
            using var safe = JsonDocument.Parse(output.ToArray());
            return safe.RootElement.Clone();
        }
        catch (JsonException) { return JsonSerializer.SerializeToElement(new { }); }
    }

    private static void WriteSafeMetadata(Utf8JsonWriter writer, JsonElement value, int depth)
    {
        if (depth > 32)
        { writer.WriteNullValue(); return; }
        if (value.ValueKind == JsonValueKind.Object)
        {
            // Some exporters encode a setting as {key: "api_token", value: "..."}.
            if (value.TryGetProperty("value", out _) &&
                new[] { "key", "name" }.Any(discriminator =>
                    (discriminator == "key" || !value.TryGetProperty("id", out _)) &&
                    value.TryGetProperty(discriminator, out var item) && item.ValueKind == JsonValueKind.String &&
                    SensitiveMetadataKey(item.GetString()!)))
            {
                writer.WriteNullValue();
                return;
            }
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject())
            {
                if (SensitiveMetadataKey(property.Name))
                    continue;
                writer.WritePropertyName(property.Name);
                WriteSafeMetadata(writer, property.Value, depth + 1);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray())
                WriteSafeMetadata(writer, item, depth + 1);
            writer.WriteEndArray();
        }
        else if (value.ValueKind == JsonValueKind.String &&
                 Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
        {
            writer.WriteNullValue();
        }
        else if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text &&
                 (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('[')))
        {
            try
            {
                using var nested = JsonDocument.Parse(text);
                WriteSafeMetadata(writer, nested.RootElement, depth + 1);
            }
            catch (JsonException) { writer.WriteNullValue(); }
        }
        else
            value.WriteTo(writer);
    }

    private static bool SensitiveMetadataKey(string name)
    {
        var key = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        // Users are retained only via the explicit typed account allowlist above; config can include SMTP/OAuth keys.
        if (key is "users" or "accounts" or "config" or "configuration" or "tracking" or "ipaddress" or "ip")
            return true;
        if (key.StartsWith("reset", StringComparison.Ordinal))
            return true;
        return new[] { "password", "passwd", "token", "secret", "session", "nonce", "credential",
            "authorization", "authentication", "cookie", "apikey", "privatekey", "accesskey", "invitecode", "oauth" }
            .Any(key.Contains);
    }

    private static string SafeResult(string result) => result.ToLowerInvariant() switch
    {
        "correct" or "solve" or "solved" or "accepted" => "correct",
        "incorrect" or "wrong" or "wrong_answer" or "rejected" => "incorrect",
        "partial" or "pending" or "discard" or "ratelimited" => result.ToLowerInvariant(),
        _ => "unknown"
    };
}
