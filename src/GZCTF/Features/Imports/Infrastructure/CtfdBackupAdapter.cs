using System.Security.Cryptography;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GZCTF.Features.Imports.Application;
using GZCTF.Utils;
using static GZCTF.Features.Imports.Infrastructure.ArchiveJson;

namespace GZCTF.Features.Imports.Infrastructure;

internal static partial class CtfdBackupAdapter
{
    internal static PlatformImportPackage Read(Dictionary<string, byte[]> files, string fingerprint)
    {
        var issues = new List<PlatformImportIssue>();
        var tables = new Dictionary<string, JsonElement[]>(StringComparer.Ordinal);
        foreach (var (path, bytes) in files.Where(pair => pair.Key.StartsWith("db/", StringComparison.Ordinal) && pair.Key.EndsWith(".json", StringComparison.Ordinal)))
        {
            var table = path[3..^5];
            try
            {
                if (bytes.Length == 0)
                {
                    tables[table] = [];
                    continue;
                }
                var document = Parse(bytes);
                var rows = Get(document, "results");
                if (rows.ValueKind != JsonValueKind.Array || Array(rows).Any(row => row.ValueKind != JsonValueKind.Object))
                    throw new JsonException();
                tables[table] = Array(rows).ToArray();
            }
            catch (JsonException)
            {
                Error(issues, "malformed_table", "", "A backup database table is malformed. Re-export a full CTFd backup.");
            }
        }
        foreach (var (table, rows) in tables)
            if (rows.Length > 0 && !MetadataTables.Contains(table) && !DiscardedTables.Contains(table) &&
                !table.StartsWith("alembic_", StringComparison.OrdinalIgnoreCase))
                Warning(issues, "unmapped_backup_table", table,
                    "This nonempty backup table is not mapped to training content or accounts. Its data will not be imported; migrate it separately if needed.");
        if (tables.Keys.Any(table => table.StartsWith("whale_", StringComparison.Ordinal)))
            Warning(issues, "whale_infrastructure_ignored", "", "Whale live containers, generated flags, routing templates and control settings are not imported. Configure the destination container host independently.");
        JsonElement[] Rows(string table) => tables.GetValueOrDefault(table) ?? [];
        var challenges = new List<PlatformImportChallenge>();
        var challengeIds = new HashSet<string>(StringComparer.Ordinal);
        var flagRows = Rows("flags");
        var fileRows = Rows("files");
        var hintRows = Rows("hints");
        var flagsByChallenge = flagRows.ToLookup(flag => Id(flag, "challenge_id"), StringComparer.Ordinal);
        var filesByChallenge = fileRows.ToLookup(file => Id(file, "challenge_id"), StringComparer.Ordinal);
        var hintsByChallenge = hintRows.ToLookup(hint => Id(hint, "challenge_id"), StringComparer.Ordinal);
        var scoringByChallenge = Rows("dynamic_challenge").ToLookup(score => Id(score), StringComparer.Ordinal);
        var whaleByChallenge = Rows("dynamic_docker_challenge").ToLookup(runtime => Id(runtime), StringComparer.Ordinal);
        foreach (var table in new[] { "flags", "files", "hints", "dynamic_challenge", "dynamic_docker_challenge" })
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in Rows(table))
                Unique(row, ids, issues, table);
        }
        foreach (var row in Rows("challenges"))
        {
            if (!Unique(row, challengeIds, issues, "challenges"))
                continue;
            var id = Id(row);
            var type = Str(row, "type") ?? "";
            var whale = type == "dynamic_docker";
            if (type is not "standard" and not "dynamic" and not "dynamic_docker")
                Error(issues, "unsupported_challenge_type", id,
                    "This challenge uses a plugin with unsupported runtime semantics. Convert it to a standard challenge or configure its container manually before importing.");
            if (Str(row, "logic") is { } logic && logic != "any")
                Error(issues, "unsupported_flag_logic", id, "This challenge requires a nonstandard flag combination. Configure supported flag validation before importing.");
            var title = Str(row, "name") ?? "";
            if (title.Length == 0)
                Error(issues, "missing_title", id, "The challenge needs a title before import.");
            var flags = new List<PlatformImportFlag>();
            foreach (var flag in flagsByChallenge[id])
            {
                if (Str(flag, "type") != "static")
                {
                    Error(issues, "unsupported_flag_type", id, "A flag uses regex or plugin validation. Convert it to supported static flags before importing.");
                    continue;
                }
                var content = Str(flag, "content");
                if (string.IsNullOrEmpty(content))
                {
                    Error(issues, "invalid_flag", id, "A static flag is empty. Configure it before importing.");
                    continue;
                }
                var data = Get(flag, "data");
                var text = Text(data);
                var ignoreCase = text == "case_insensitive" || Bool(data, "case_insensitive");
                if (!string.IsNullOrEmpty(text) && text != "case_insensitive")
                    Error(issues, "unsupported_flag_configuration", id, "A static flag contains an unknown validation setting. Review its case matching configuration before importing.");
                flags.Add(new("static", content, ignoreCase));
            }
            var missingFlags = flags.Count == 0 && !whale;
            if (missingFlags)
                Warning(issues, "missing_flags_disabled", id, "This challenge has no supported flag and is imported as a disabled draft. Configure a flag before publishing it.");
            PlatformImportContainer? container = null;
            if (whale)
            {
                var runtime = whaleByChallenge[id].FirstOrDefault();
                container = WhaleContainer(runtime, id, issues);
                // Whale gives configured static flags precedence; without them it validates the instance flag.
                Warning(issues, "whale_runtime_recreated", id, "Whale is converted to a per-learner container with a fresh flag passed as FLAG. Source proxy routes and live instance flags are not reused; verify the destination image and access endpoint.");
            }
            if (type == "dynamic" || whale && Int(whaleByChallenge[id].FirstOrDefault(), "dynamic_score") != 0)
                Warning(issues, "dynamic_scoring_becomes_training_score", id, "The current source value becomes a fixed training score. Original initial, minimum and decay values remain in private metadata; the competition scoring curve is not replayed.");
            var attachments = new List<PlatformImportAttachment>();
            foreach (var file in filesByChallenge[id])
            {
                var location = Str(file, "location") ?? "";
                AddFile(location, null, id, files, attachments, issues, Str(file, "sha1sum"));
            }
            var body = Str(row, "description") ?? "";
            foreach (Match match in BodyFileReference().Matches(body))
            {
                var original = match.Value;
                var location = match.Groups[1].Value;
                // Paths in Markdown remain data: do not resolve hosts, URLs or filesystem paths.
                AddFile(location, original, id, files, attachments, issues);
            }
            var hints = hintsByChallenge[id].ToArray();
            if (hints.Any(hint => Int(hint, "cost") > 0))
                Warning(issues, "paid_hints_become_training_hints", id, "Paid hints are preserved as training hints; original hint costs remain in the private import metadata.");
            var metadata = JsonSerializer.Serialize(new
            {
                challenge = SafeMetadata(row),
                flags = flagsByChallenge[id].Select(flag => SafeMetadata(flag)),
                hints = hints.Select(hint => SafeMetadata(hint)),
                files = filesByChallenge[id].Select(file => SafeMetadata(file)),
                scoring = scoringByChallenge[id].Select(score => SafeMetadata(score)),
                whaleRuntime = whaleByChallenge[id].Select(runtime => SafeMetadata(runtime))
            });
            var score = Integer(Get(row, "value"));
            if (!score.HasValue || score < 0)
                Error(issues, "invalid_challenge_score", id, "A challenge score is missing or invalid. Configure the training score before importing.");
            challenges.Add(new(id, title, body, Str(row, "category") ?? "Misc", whale ? flags.Count > 0 ? ChallengeType.StaticContainer : ChallengeType.DynamicContainer : ChallengeType.StaticAttachment,
                "Normal", score ?? 0, Str(row, "state") == "visible" && !missingFlags, Int(row, "max_attempts"), flags,
                hints.Select(hint => Str(hint, "content") ?? "").ToArray(), attachments, container, metadata));
        }
        if (challenges.Count == 0)
            Error(issues, "empty_challenges", "", "The backup contains no readable challenges.");
        foreach (var row in flagRows.Concat(hintRows))
            if (!challengeIds.Contains(Id(row, "challenge_id")))
                Error(issues, "unknown_challenge_reference", Id(row), "A flag or hint refers to a missing challenge. Repair the backup reference before importing.");
        var teams = new List<PlatformImportTeam>();
        var teamIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Rows("teams"))
            if (Unique(row, teamIds, issues, "teams"))
                teams.Add(new(Id(row), Str(row, "name") ?? "", SafeRaw(row, TeamMetadataFields)));
        var users = new List<PlatformImportUser>();
        var userIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Rows("users"))
        {
            if (!Unique(row, userIds, issues, "users"))
                continue;
            var teamId = Str(row, "team_id");
            if (teamId is not null && !teamIds.Contains(teamId))
                Error(issues, "unknown_team_reference", Id(row), "A user refers to a missing team. Repair the backup reference before importing.");
            var name = Str(row, "name") ?? "";
            // Effective usernames are validated after explicit rename/skip options are applied.
            users.Add(new(Id(row), name, Str(row, "email"), Str(row, "password"), Bool(row, "verified"),
                Bool(row, "banned"), teamId, Date(row, "created"), SafeRaw(row, UserMetadataFields)));
        }
        var submissions = new Dictionary<string, PlatformImportSubmission>(StringComparer.Ordinal);
        var submissionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Rows("submissions"))
        {
            if (!Unique(row, submissionIds, issues, "submissions"))
                continue;
            var result = Str(row, "type") ?? "unknown";
            submissions.Add(Id(row), Submission(row, result));
        }
        var solveIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Rows("solves"))
        {
            if (!Unique(row, solveIds, issues, "solves"))
                continue;
            var id = Id(row);
            var solve = Submission(row, "correct");
            if (submissions.TryGetValue(id, out var existing))
            {
                if (existing.ChallengeId != solve.ChallengeId || existing.UserId != solve.UserId || existing.TeamId != solve.TeamId || existing.Result != "correct")
                    Error(issues, "conflicting_solve_reference", id, "A solve conflicts with its submission record. Repair the backup history before importing.");
                if (existing.Score is not null && solve.Score is not null && existing.Score != solve.Score)
                    Error(issues, "conflicting_history_score", id, "A solve and its submission contain different stored scores. Repair the source history before importing.");
                submissions[id] = existing with
                {
                    Score = existing.Score ?? solve.Score,
                    SubmittedAtUtc = existing.SubmittedAtUtc ?? solve.SubmittedAtUtc,
                    MetadataJson = JsonSerializer.Serialize(new { submission = Parse(System.Text.Encoding.UTF8.GetBytes(existing.MetadataJson)), solve = SafeMetadata(row) })
                };
            }
            else
                submissions.Add(id, solve);
        }
        foreach (var submission in submissions.Values)
            ValidateReferences(submission.SourceId, submission.UserId, submission.TeamId, submission.ChallengeId);
        if (submissions.Values.Any(submission => submission.Result == "correct" && submission.Score is null))
            Warning(issues, "historical_score_unavailable", "", "Some solves have no stored historical score. They remain unscored history; current challenge values will not be substituted.");
        var awards = new List<PlatformImportAward>();
        var awardIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Rows("awards"))
        {
            if (!Unique(row, awardIds, issues, "awards"))
                continue;
            ValidateReferences(Id(row), Str(row, "user_id"), Str(row, "team_id"), null);
            var value = Integer(Get(row, "value"));
            if (!value.HasValue)
                Error(issues, "invalid_award_score", Id(row), "An award value is missing or invalid. Repair it before importing.");
            awards.Add(new(Id(row), Str(row, "user_id"), Str(row, "team_id"), Str(row, "name") ?? "Award",
                value ?? 0, Date(row, "date"), SafeRaw(row)));
        }
        var titleConfig = Rows("config").FirstOrDefault(row => Str(row, "key") == "ctf_name");
        return new("CTFd", fingerprint, Str(titleConfig, "value") ?? "Imported CTFd backup", challenges,
            users, teams, submissions.Values.ToArray(), awards,
            files.Where(pair => pair.Key.StartsWith("uploads/", StringComparison.Ordinal))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), issues, PackageMetadata(tables));

        PlatformImportSubmission Submission(JsonElement row, string result) =>
            new(Id(row), Str(row, "user_id"), Str(row, "team_id"), Id(row, "challenge_id"), result,
                Date(row, "date"), Integer(Get(row, "value")) ?? Integer(Get(row, "score")), SafeRaw(row));

        void ValidateReferences(string id, string? user, string? team, string? challenge)
        {
            if (user is null && team is null)
                Error(issues, "missing_history_actor", id, "A history record has no user or team. Repair its source ownership before importing.");
            if (user is not null && !userIds.Contains(user))
                Error(issues, "unknown_user_reference", id, "A history record refers to a missing user. Include the user in the backup.");
            if (team is not null && !teamIds.Contains(team))
                Error(issues, "unknown_team_reference", id, "A history record refers to a missing team. Include the team in the backup.");
            if (challenge is not null && !challengeIds.Contains(challenge))
                Error(issues, "unknown_challenge_reference", id, "A history record refers to a missing challenge. Include the challenge in the backup.");
        }
    }

    private static readonly HashSet<string> UserMetadataFields = new(StringComparer.OrdinalIgnoreCase)
    { "id", "name", "email", "team_id", "verified", "banned", "hidden", "created", "country", "affiliation", "website", "type", "bio", "language" };
    private static readonly HashSet<string> TeamMetadataFields = new(StringComparer.OrdinalIgnoreCase)
    { "id", "name", "email", "hidden", "banned", "created", "country", "affiliation", "website", "type", "bio", "language" };
    private static readonly HashSet<string> MetadataTables = new(StringComparer.Ordinal)
    { "challenges", "flags", "hints", "files", "users", "teams", "submissions", "solves", "awards", "dynamic_challenge", "dynamic_docker_challenge", "config", "tags" };
    private static readonly HashSet<string> DiscardedTables = new(StringComparer.OrdinalIgnoreCase)
    { "tokens", "sessions", "tracking", "api_tokens", "auth_tokens", "access_tokens", "refresh_tokens",
        "whale_config", "whale_container", "whale_redirect_template", "oauth_tokens", "oauth_sessions", "user_sessions", "login_tracking", "password_resets", "password_reset_tokens" };
    private static readonly HashSet<string> SafeConfigKeys = new(StringComparer.Ordinal)
    { "ctf_name", "ctf_description", "user_mode", "start", "end", "freeze", "challenge_visibility", "account_visibility",
        "score_visibility", "registration_visibility", "verify_emails", "team_size", "max_attempts" };

    private static string PackageMetadata(Dictionary<string, JsonElement[]> tables)
    {
        var metadata = new Dictionary<string, JsonElement[]>(StringComparer.Ordinal);
        foreach (var (table, rows) in tables.Where(table => MetadataTables.Contains(table.Key)))
        {
            var selected = table == "config" ? rows.Where(row => SafeConfigKeys.Contains(Str(row, "key") ?? "")) : rows;
            metadata[table] = selected.Select(row => SafeMetadata(row,
                table == "users" ? UserMetadataFields : table == "teams" ? TeamMetadataFields : null)).ToArray();
        }
        return JsonSerializer.Serialize(metadata);
    }

    private static PlatformImportContainer WhaleContainer(JsonElement runtime, string id, List<PlatformImportIssue> issues)
    {
        var image = Str(runtime, "docker_image") ?? "";
        var port = Int(runtime, "redirect_port");
        var cpuText = Str(runtime, "cpu_limit");
        var cpu = decimal.TryParse(cpuText, NumberStyles.Float, CultureInfo.InvariantCulture, out var cores) &&
            cores > 0 && cores <= int.MaxValue / 10m && cores * 10 == decimal.Truncate(cores * 10)
            ? (int)(cores * 10) : 0;
        var memory = ParseWhaleMemory(Str(runtime, "memory_limit"));
        if (string.IsNullOrWhiteSpace(image) || image.Any(char.IsWhiteSpace) || image.StartsWith('{') ||
            image.Contains("://", StringComparison.Ordinal) || port is < 1 or > 65535 || cpu <= 0 || memory <= 0 ||
            Str(runtime, "redirect_type") is not ("http" or "direct"))
            Error(issues, "invalid_whale_runtime", id, "A Whale challenge needs a single container image, HTTP or direct port, positive memory and CPU in 0.1-core increments. Grouped images and custom routing need manual conversion.");
        return new(image, port, cpu, memory, 256, "Open", "ctfd-whale");
    }

    private static int ParseWhaleMemory(string? text)
    {
        var match = WhaleMemory().Match(text ?? "");
        if (!match.Success || !decimal.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value))
            return 0;
        var multiplier = match.Groups[2].Value.ToLowerInvariant() switch
        {
            "k" => 1m / 1024, "m" => 1m, "g" => 1024m, "t" => 1048576m, _ => 1m / 1048576
        };
        if (value <= 0 || value > int.MaxValue / multiplier)
            return 0;
        var mb = decimal.Ceiling(value * multiplier);
        return mb > 0 && mb <= int.MaxValue ? (int)mb : 0;
    }

    [GeneratedRegex(@"^([0-9]+(?:\.[0-9]+)?)\s*([kmgt]?)(?:i?b)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WhaleMemory();

    private static void AddFile(string location, string? originalUrl, string id, Dictionary<string, byte[]> files,
        List<PlatformImportAttachment> attachments, List<PlatformImportIssue> issues, string? expectedSha1 = null)
    {
        if (IsWebLink(originalUrl) && !files.ContainsKey("uploads/" + location))
        {
            Warning(issues, "external_body_dependency", id,
                "A body image or file remains an external HTTP or HTTPS dependency. Its original link is preserved without fetching its contents.");
            return;
        }
        if (!IsSafePath(location))
        {
            Error(issues, "unsafe_attachment_path", id, "An attachment location is not a safe relative upload path. Repair the backup file reference.");
            return;
        }
        var path = "uploads/" + location;
        if (!files.TryGetValue(path, out var bytes))
        {
            Error(issues, "missing_attachment", id, "An attachment or body asset is absent from the backup. Include the referenced upload before importing.");
            return;
        }
        if (!string.IsNullOrEmpty(expectedSha1) && !string.Equals(expectedSha1,
                Convert.ToHexStringLower(SHA1.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
            Error(issues, "attachment_hash_mismatch", id, "An upload does not match its stored checksum. Re-export the complete backup.");
        var existing = attachments.FindIndex(attachment => attachment.ArchivePath == path);
        var attachment = new PlatformImportAttachment(path, location.Split('/')[^1],
            Convert.ToHexStringLower(SHA256.HashData(bytes)), IsBodyResource: originalUrl is not null);
        if (existing < 0)
            attachments.Add(originalUrl is null ? attachment : WithBodyAlias(attachment, originalUrl));
        else if (originalUrl is not null)
            attachments[existing] = WithBodyAlias(attachments[existing], originalUrl);
        else
            attachments[existing] = attachments[existing] with { IsBodyResource = false };
    }

    [GeneratedRegex("""(?:https?://[^/\s\)\]"'<>]+(?:/[^/\s\)\]"'<>?#]+)*)?(?:/api)?/files/([^\s\)\]"'<>?\#]+)(?:[?#][^\s\)\]"'<>]*)?""", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BodyFileReference();
}
