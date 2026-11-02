using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GZCTF.Features.Imports.Application;
using GZCTF.Utils;
using static GZCTF.Features.Imports.Infrastructure.ArchiveJson;

namespace GZCTF.Features.Imports.Infrastructure;

internal static partial class GzctfGameAdapter
{
    internal static PlatformImportPackage Read(Dictionary<string, byte[]> files, string fingerprint)
    {
        var issues = new List<PlatformImportIssue>();
        JsonElement manifest;
        JsonElement game;
        try
        {
            manifest = Parse(files["manifest.json"]);
            if (Str(manifest, "format") != "GZCTF-GAME" || Str(manifest, "version") != "1.0")
                throw new InvalidDataException("import.unsupported_gzctf_version");
            game = Parse(files.TryGetValue("game.json", out var data) ? data : throw new InvalidDataException("import.missing_game"));
        }
        catch (JsonException)
        {
            throw new InvalidDataException("import.malformed_manifest_or_game");
        }
        var posterHash = Str(game, "posterHash");
        if (!string.IsNullOrEmpty(posterHash))
        {
            posterHash = posterHash.ToLowerInvariant();
            if (posterHash.Length != 64 || !posterHash.All(Uri.IsHexDigit))
                Error(issues, "invalid_attachment", "game", "The game poster requires a valid SHA-256 hash.");
            else if (!files.TryGetValue("files/" + posterHash, out var poster))
                Error(issues, "missing_attachment", "game", "The game poster is absent from the backup. Include the referenced file before importing.");
            else if (!HashMatches(posterHash, poster))
                Error(issues, "attachment_hash_mismatch", "game", "The game poster does not match its SHA-256 hash. Re-export the backup.");
        }
        var challenges = new List<PlatformImportChallenge>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, data) in files.Where(pair => pair.Key.StartsWith("challenges/", StringComparison.Ordinal) && pair.Key.EndsWith(".json", StringComparison.Ordinal)).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            JsonElement row;
            try
            { row = Parse(data); }
            catch (JsonException)
            {
                Error(issues, "malformed_challenge", "", "A challenge JSON document is malformed. Re-export or repair the backup.");
                continue;
            }
            if (!Unique(row, ids, issues, "challenges"))
                continue;
            var id = Id(row);
            var title = Str(row, "title") ?? "";
            var category = Str(row, "category") ?? "Misc";
            if (title.Length == 0)
                Error(issues, "missing_title", id, "The challenge needs a title before import.");
            var typeText = Str(row, "type");
            if (!Enum.TryParse<ChallengeType>(typeText, true, out var type) || !Enum.IsDefined(type))
            {
                Error(issues, "unsupported_challenge_type", id, "The challenge type is unsupported. Convert it to a standard GZCTF challenge type.");
                type = ChallengeType.StaticAttachment;
            }
            var flagData = Get(row, "flags");
            var flags = new List<PlatformImportFlag>();
            var attachments = new List<PlatformImportAttachment>();
            foreach (var flag in Array(Get(flagData, "static")))
            {
                var value = Str(flag, "value");
                if (string.IsNullOrEmpty(value))
                    Error(issues, "invalid_flag", id, "A static flag is empty. Configure it before importing.");
                else
                    flags.Add(new("static", value));
                AddAttachment(Get(flag, "attachment"), value, id, files, attachments, issues);
            }
            var template = Str(flagData, "template");
            if (!string.IsNullOrEmpty(template))
            {
                flags.Add(new("template", template));
                if (TemplateMarker().Matches(template).Cast<Match>().Any(marker =>
                        marker.Value is not "[GUID]" and not "[TEAM_HASH]" &&
                        !(marker.Index == 0 && marker.Value is "[LEET]" or "[CLEET]")))
                    Warning(issues, "unrecognized_template_marker", id,
                        "The flag template contains an unrecognized bracket marker. Its source text and original GZCTF behavior are preserved; review the generated flag before publishing.");
            }
            PlatformImportContainer? container = null;
            if (type.IsContainer())
            {
                var config = Get(row, "container");
                var image = Str(config, "image") ?? "";
                var port = Int(config, "exposePort", 80);
                var cpu = Int(config, "cpuCount", 1);
                var memory = Int(config, "memoryLimit", 64);
                var storage = Int(config, "storageLimit", 256);
                if (image.Length == 0 || port is < 1 or > 65535 || cpu < 1 || memory < 1 || storage < 1)
                    Error(issues, "invalid_container", id, "Container image, port and resource limits must be configured before importing.");
                var network = Str(config, "networkMode") ?? "Open";
                if (!Enum.TryParse<NetworkMode>(network, true, out var networkMode) || !Enum.IsDefined(networkMode))
                {
                    Error(issues, "unsupported_network_mode", id, "Select a supported container network mode before importing.");
                    networkMode = NetworkMode.Open;
                }
                container = new(image, port, cpu, memory, storage, networkMode.ToString().ToLowerInvariant(),
                    type == ChallengeType.DynamicContainer ? string.IsNullOrEmpty(template) ? "gzctf-random" : "gzctf" : "native");
            }
            AddAttachment(Get(row, "attachment"), null, id, files, attachments, issues);
            var body = Str(row, "content") ?? "";
            foreach (Match match in AssetReference().Matches(body))
            {
                var hash = match.Groups[1].Value.ToLowerInvariant();
                var archivePath = "files/" + hash;
                var existing = attachments.Select((attachment, index) => (attachment, index))
                    .Where(item => item.attachment.ArchivePath == archivePath).Select(item => item.index).ToArray();
                if (existing.Length > 0)
                {
                    foreach (var index in existing)
                        attachments[index] = WithBodyAlias(attachments[index], match.Value);
                    continue;
                }
                if (!files.TryGetValue(archivePath, out var asset))
                {
                    if (IsWebLink(match.Value))
                        Warning(issues, "external_body_dependency", id,
                            "A body image or file remains an external HTTP or HTTPS dependency. Its original link is preserved without fetching its contents.");
                    else
                        Error(issues, "missing_body_asset", id, "A body image or file is absent from the backup. Include the referenced asset before importing.");
                    continue;
                }
                if (!HashMatches(hash, asset))
                {
                    Error(issues, "attachment_hash_mismatch", id, "A referenced asset does not match its SHA-256 hash. Re-export the backup.");
                    continue;
                }
                attachments.Add(WithBodyAlias(new(archivePath, hash, hash, IsBodyResource: true), match.Value));
            }
            if (flags.Count == 0 && type != ChallengeType.DynamicContainer)
                Error(issues, "missing_flags", id, "Configure the challenge flags before importing.");
            if (type == ChallengeType.DynamicAttachment && attachments.All(a => a.Flag is null))
                Error(issues, "missing_dynamic_attachments", id, "Dynamic attachments require a file assigned to each static flag.");
            challenges.Add(new(id, title, body, category, type, Str(row, "difficultyLevel") ?? "Normal",
                Int(Get(row, "scoring"), "original", 1000), Bool(row, "enabled"), Int(Get(row, "limits"), "submission"),
                flags, Array(Get(row, "hints")).Select(Text).OfType<string>().ToArray(), attachments, container, SafeRaw(row)));
        }
        if (challenges.Count == 0)
            Error(issues, "empty_challenges", "", "The backup contains no readable challenge documents.");
        var expected = Integer(Get(Get(manifest, "statistics"), "challengeCount"));
        if (expected.HasValue && expected != challenges.Count)
            Error(issues, "challenge_count_mismatch", "", "The challenge count differs from the manifest. Re-export the complete game.");
        return new("GZCTF-GAME", fingerprint, Str(game, "title") ?? Str(Get(manifest, "game"), "title") ?? "Imported game",
            challenges, [], [], [], [], files.Where(pair => pair.Key.StartsWith("files/", StringComparison.Ordinal))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), issues, JsonSerializer.Serialize(new { manifest = SafeMetadata(manifest), game = SafeMetadata(game) }));
    }

    private static void AddAttachment(JsonElement value, string? flag, string id, Dictionary<string, byte[]> files,
        List<PlatformImportAttachment> attachments, List<PlatformImportIssue> issues)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return;
        var type = Str(value, "type") ?? "Local";
        if (type is "None" or "0")
            return;
        if (type.Equals("Remote", StringComparison.OrdinalIgnoreCase) || type == "2")
        {
            var remoteUrl = Str(value, "remoteUrl");
            if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            {
                Error(issues, "invalid_remote_attachment_url", id, "A remote attachment requires an absolute HTTP or HTTPS link without embedded credentials.");
                return;
            }
            if (flag is not null)
            {
                Error(issues, "remote_dynamic_attachment_unsupported", id, "Per-flag remote attachments cannot be assigned safely. Include each file locally before importing this dynamic attachment challenge.");
                return;
            }
            attachments.Add(new("", Str(value, "fileName") ?? "attachment", "", RemoteUrl: remoteUrl));
            Warning(issues, "remote_attachment_link_preserved", id, "A remote attachment is preserved as an external download link. Its contents are not fetched or copied.");
            return;
        }
        if (!type.Equals("Local", StringComparison.OrdinalIgnoreCase) && type != "1")
        {
            Error(issues, "unsupported_attachment_type", id, "Choose a local attachment type before importing.");
            return;
        }
        var hash = Str(value, "hash")?.ToLowerInvariant() ?? "";
        var name = Str(value, "fileName") ?? "";
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(name))
        {
            Error(issues, "invalid_attachment", id, "A local attachment requires a file name and valid SHA-256 hash.");
            return;
        }
        var path = "files/" + hash;
        if (!files.TryGetValue(path, out var bytes))
            Error(issues, "missing_attachment", id, "An attachment is absent from the backup. Include the referenced file before importing.");
        else if (!HashMatches(hash, bytes))
            Error(issues, "attachment_hash_mismatch", id, "An attachment does not match its SHA-256 hash. Re-export the backup.");
        else
            attachments.Add(new(path, name, hash, flag));
    }

    private static bool HashMatches(string hash, byte[] bytes) => string.Equals(hash,
        Convert.ToHexStringLower(SHA256.HashData(bytes)), StringComparison.Ordinal);

    [GeneratedRegex(@"\[[A-Za-z_][A-Za-z0-9_]*\]", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateMarker();

    [GeneratedRegex("""(?:https?://[^/\s\)\]"'<>]+(?:/[^/\s\)\]"'<>?#]+)*)?(?:/api)?/(?:assets|files)/([a-fA-F0-9]{64})(?![a-fA-F0-9])(?:/[^\s\)\]"'<>?#]*)?(?:[?#][^\s\)\]"'<>]*)?""", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AssetReference();
}
