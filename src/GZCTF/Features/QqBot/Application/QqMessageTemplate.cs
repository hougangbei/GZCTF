using System.Text.RegularExpressions;

namespace GZCTF.Features.QqBot.Application;

public sealed record QqSolveEvent(string Member, string Challenge, string Source, string Team = "");

public static partial class QqMessageTemplate
{
    private static readonly HashSet<string> Tokens = ["member", "challenge", "source", "team", "time"];

    public static bool IsValid(string template)
    {
        if (string.IsNullOrWhiteSpace(template)) return false;
        var withoutTokens = Placeholder().Replace(template, match =>
            Tokens.Contains(match.Groups[1].Value) ? "" : "{" );
        return !withoutTokens.Contains('{') && !withoutTokens.Contains('}');
    }

    public static string Render(string template, QqSolveEvent solve)
    {
        if (!IsValid(template)) throw new ArgumentException("Invalid message template", nameof(template));
        return Placeholder().Replace(template, match => match.Groups[1].Value switch
        {
            "member" => EscapeCq(solve.Member),
            "challenge" => EscapeCq(solve.Challenge),
            "source" => EscapeCq(solve.Source),
            "team" => EscapeCq(solve.Team),
            "time" => DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            _ => string.Empty
        });
    }

    internal static string EscapeCq(string text) => text.Replace("&", "&amp;")
        .Replace("[", "&#91;").Replace("]", "&#93;");

    [GeneratedRegex(@"\{([a-zA-Z]+)\}")]
    private static partial Regex Placeholder();
}
