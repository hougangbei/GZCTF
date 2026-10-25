using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GZCTF.Features.Updates.Application;

/// <summary>Administrator-selected source; persisted by ConfigService without deployment secrets.</summary>
public sealed class UpdateSourceSettings
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "github";
    [JsonPropertyName("location")]
    public string Location { get; set; } = "https://github.com/hougangbei/GZCTF";
    [JsonPropertyName("ref")]
    public string Ref { get; set; } = "main";
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";
    [JsonPropertyName("generation")]
    public string Generation { get; set; } = "";

    public static bool TryNormalize(UpdateSourceSettings? source, out UpdateSourceSettings normalized)
    {
        normalized = new();
        if (source is null || source.Kind is null || source.Location is null ||
            source.Ref is null || source.Sha256 is null || source.Generation is null)
            return false;
        if (source.Generation.Length != 0 &&
            !Regex.IsMatch(source.Generation, "\\A[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
            return false;
        var kind = source.Kind.Trim().ToLowerInvariant();
        var location = source.Location.Trim();
        var branch = source.Ref.Trim();
        var sha256 = source.Sha256.Trim().ToLowerInvariant();
        if (location.Length is 0 or > 2048 || location.Any(char.IsControl) ||
            branch.Length > 200 || sha256.Length > 64 ||
            sha256.Length != 0 && !Regex.IsMatch(sha256, "\\A[0-9a-f]{64}\\z"))
            return false;

        if (kind == "directory")
        {
            if (!location.StartsWith('/') || branch.Length != 0 || sha256.Length != 0)
                return false;
            try { location = Path.GetFullPath(location).TrimEnd('/'); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            { return false; }
            if (location.Length == 0) location = "/";
        }
        else if (kind is "github" or "package")
        {
            if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host) ||
                uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || location.Contains('\\'))
                return false;
            if (kind == "github")
            {
                if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                    !uri.IsDefaultPort || uri.Query.Length != 0 || sha256.Length != 0)
                    return false;
                var parts = uri.AbsolutePath.Trim('/').Split('/');
                if (parts.Length < 2 || !Regex.IsMatch(parts[0], "\\A[A-Za-z0-9_-]+\\z"))
                    return false;
                var repo = parts[1].EndsWith(".git", StringComparison.Ordinal) ? parts[1][..^4] : parts[1];
                if (!Regex.IsMatch(repo, "\\A[A-Za-z0-9_.-]+\\z") || repo is "." or "..")
                    return false;
                if (parts.Length > 2)
                {
                    if (parts.Length < 4 || parts[2] != "tree") return false;
                    branch = string.Join('/', parts.Skip(3));
                }
                if (branch.Length == 0) branch = "main";
                if (!SafeRef(branch)) return false;
                location = $"https://github.com/{parts[0]}/{repo}";
            }
            else
            {
                if (branch.Length != 0) return false;
                location = uri.AbsoluteUri;
            }
        }
        else return false;

        normalized = new() { Kind = kind, Location = location, Ref = branch, Sha256 = sha256, Generation = source.Generation };
        return true;
    }

    private static bool SafeRef(string value) => value.Length <= 200 &&
        Regex.IsMatch(value, "\\A[A-Za-z0-9][A-Za-z0-9._/-]*\\z") &&
        !value.Contains("..", StringComparison.Ordinal) && !value.Contains("//", StringComparison.Ordinal) &&
        !value.EndsWith('/') && !value.EndsWith('.') &&
        value.Split('/').All(part => !part.StartsWith('.') && !part.EndsWith(".lock", StringComparison.Ordinal));
}
