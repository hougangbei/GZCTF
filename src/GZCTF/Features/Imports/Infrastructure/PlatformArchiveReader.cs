using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Features.Imports.Application;

namespace GZCTF.Features.Imports.Infrastructure;

/// <summary>Reads untrusted platform backups without extracting or contacting external services.</summary>
public sealed class PlatformArchiveReader
{
    public const long MaxCompressedBytes = 256L * 1024 * 1024;
    public const long MaxExpandedBytes = 256L * 1024 * 1024;
    public const int MaxFiles = 10000;

    public async Task<PlatformImportPackage> ReadAsync(Stream source, CancellationToken cancellationToken = default)
    {
        using var compressed = new MemoryStream();
        await CopyBoundedAsync(source, compressed, MaxCompressedBytes, cancellationToken);
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(compressed.GetBuffer().AsSpan(0, (int)compressed.Length)));
        compressed.Position = 0;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            using var archive = new ZipArchive(compressed, ZipArchiveMode.Read, true);
            if (archive.Entries.Count > MaxFiles)
                throw new InvalidDataException("import.too_many_files");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = entry.FullName;
                var directory = path.EndsWith('/');
                if (!ArchiveJson.IsSafePath(directory ? path[..^1] : path) || !paths.Add(path.TrimEnd('/')))
                    throw new InvalidDataException("import.unsafe_or_duplicate_archive_path");
                // Unix symlinks must never be interpreted as files or followed by a later consumer.
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException("import.archive_symlink");
                if (directory)
                    continue;
                if (entry.Length > MaxExpandedBytes - expanded)
                    throw new InvalidDataException("import.expanded_archive_too_large");
                using var output = new MemoryStream();
                await using var input = entry.Open();
                await CopyBoundedAsync(input, output, MaxExpandedBytes - expanded, cancellationToken);
                expanded += output.Length;
                files.Add(path, output.ToArray());
            }
        }
        catch (InvalidDataException exception) when (!exception.Message.StartsWith("import.", StringComparison.Ordinal))
        {
            throw new InvalidDataException("import.invalid_or_unsafe_archive");
        }
        if (files.ContainsKey("manifest.json"))
            return GzctfGameAdapter.Read(files, fingerprint);
        if (files.ContainsKey("db/challenges.json"))
            return CtfdBackupAdapter.Read(files, fingerprint);
        throw new InvalidDataException("import.unsupported_archive_format");
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, long limit, CancellationToken token)
    {
        var buffer = new byte[81920];
        long count = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, token);
            if (read == 0)
                break;
            count += read;
            if (count > limit)
                throw new InvalidDataException("import.archive_size_limit");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
    }
}

internal static class ArchiveJson
{
    internal static bool IsSafePath(string? path) => !string.IsNullOrWhiteSpace(path) &&
        !path.StartsWith('/') && !path.Contains('\\') && !path.Contains(':') && !path.Contains('\0') &&
        path.Split('/').All(part => part.Length > 0 && part is not "." and not "..");

    internal static bool IsWebLink(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);

    internal static JsonElement Parse(byte[] data)
    {
        using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 64 });
        return document.RootElement.Clone();
    }

    internal static JsonElement Get(JsonElement value, string property)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var member in value.EnumerateObject())
                if (string.Equals(member.Name, property, StringComparison.OrdinalIgnoreCase))
                    return member.Value;
        return default;
    }

    internal static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null
    };
    internal static string? Str(JsonElement value, string property) => Text(Get(value, property));
    internal static string Id(JsonElement value, string property = "id") => Str(value, property) ?? "";
    internal static int? Integer(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
        ? number : int.TryParse(Text(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ? number : null;
    internal static int Int(JsonElement value, string property, int fallback = 0) => Integer(Get(value, property)) ?? fallback;
    internal static bool Bool(JsonElement value, string property, bool fallback = false) => Get(value, property).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => Integer(Get(value, property)) is { } number && number != 0,
        _ => bool.TryParse(Str(value, property), out var result) ? result : fallback
    };
    internal static IEnumerable<JsonElement> Array(JsonElement value) => value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray() : [];
    internal static DateTimeOffset? Date(JsonElement value, string property) =>
        DateTimeOffset.TryParse(Str(value, property), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : null;
    internal static string Raw(JsonElement value) => value.ValueKind == JsonValueKind.Undefined ? "{}" : value.GetRawText();
    // Preserve source/plugin data without copying credentials or live authentication state.
    internal static JsonElement SafeMetadata(JsonElement value, IReadOnlySet<string>? allowedFields = null)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
            WriteMetadata(writer, value, allowedFields);
        return Parse(output.ToArray());
    }

    internal static string SafeRaw(JsonElement value, IReadOnlySet<string>? allowedFields = null) =>
        SafeMetadata(value, allowedFields).GetRawText();

    private static void WriteMetadata(Utf8JsonWriter writer, JsonElement value, IReadOnlySet<string>? allowedFields = null, int depth = 0)
    {
        if (depth > 64)
        { writer.WriteNullValue(); return; }
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (Get(value, "value").ValueKind != JsonValueKind.Undefined &&
                ((Str(value, "key") is { } key && SensitiveField(key)) ||
                 (Get(value, "id").ValueKind == JsonValueKind.Undefined && Str(value, "name") is { } name && SensitiveField(name))))
            {
                writer.WriteNullValue();
                return;
            }
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject())
            {
                if ((allowedFields is not null && !allowedFields.Contains(property.Name)) || SensitiveField(property.Name))
                    continue;
                if (Uri.TryCreate(Text(property.Value), UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
                    continue;
                writer.WritePropertyName(property.Name);
                WriteMetadata(writer, property.Value, depth: depth + 1);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray())
                WriteMetadata(writer, item, depth: depth + 1);
            writer.WriteEndArray();
        }
        else if (value.ValueKind == JsonValueKind.String &&
                 Uri.TryCreate(Text(value), UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
            writer.WriteNullValue();
        else if (value.ValueKind == JsonValueKind.String && Text(value)?.TrimStart() is { } text &&
                 (text.StartsWith('{') || text.StartsWith('[')))
        {
            try
            {
                using var nested = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 64 });
                WriteMetadata(writer, nested.RootElement, depth: depth + 1);
            }
            catch (JsonException) { writer.WriteNullValue(); }
        }
        else if (value.ValueKind == JsonValueKind.Undefined)
            writer.WriteNullValue();
        else
            value.WriteTo(writer);
    }

    private static readonly string[] SensitiveMetadataFragments =
    ["password", "passwd", "token", "secret", "session", "nonce", "credential", "authorization", "authentication",
        "cookie", "apikey", "accesskey", "privatekey", "signingkey", "encryptionkey", "invitecode", "salt", "totp", "recoverycode"];

    private static bool SensitiveField(string name)
    {
        var normalized = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return normalized is "ip" or "ipaddress" or "tracking" || normalized.StartsWith("reset", StringComparison.Ordinal) || SensitiveMetadataFragments.Any(normalized.Contains);
    }

    internal static PlatformImportAttachment WithBodyAlias(PlatformImportAttachment attachment, string alias)
    {
        var aliases = (attachment.OriginalUrl is { } first ? new[] { first } : [])
            .Concat(attachment.OriginalUrls ?? []).Append(alias).Distinct(StringComparer.Ordinal).ToArray();
        return attachment with { OriginalUrl = aliases[0], OriginalUrls = aliases };
    }

    internal static void Error(List<PlatformImportIssue> issues, string code, string id, string message) =>
        issues.Add(new("error", code, id, message));
    internal static void Warning(List<PlatformImportIssue> issues, string code, string id, string message) =>
        issues.Add(new("warning", code, id, message));
    internal static bool Unique(JsonElement row, HashSet<string> ids, List<PlatformImportIssue> issues, string entity)
    {
        var id = Id(row);
        if (id.Length > 0 && ids.Add(id))
            return true;
        Error(issues, id.Length == 0 ? "missing_source_id" : "duplicate_source_id", id,
            $"The {entity} table contains a missing or repeated source ID. Repair the backup before importing.");
        return false;
    }
}
