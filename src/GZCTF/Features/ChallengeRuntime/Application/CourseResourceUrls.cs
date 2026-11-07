using System.Text.RegularExpressions;

namespace GZCTF.Features.ChallengeRuntime.Application;

/// <summary>Keep imported resource links on the same explicitly selected course snapshot.</summary>
internal static partial class CourseResourceUrls
{
    public static string ForCourse(string content, Guid? courseId)
    {
        if (courseId is null) return content;
        return ResourceUrl().Replace(content, match =>
        {
            var existing = match.Groups["query"].Value.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !part.StartsWith("course=", StringComparison.OrdinalIgnoreCase));
            return match.Groups["path"].Value + "?" + string.Join("&", existing.Append($"course={courseId:D}"));
        });
    }

    [GeneratedRegex("(?<path>/api/challenges/[0-9a-fA-F-]{36}/resources/[a-zA-Z0-9:_-]+)(?<query>\\?[^\\\"'\\s<>\\)]*)?")]
    private static partial Regex ResourceUrl();
}
