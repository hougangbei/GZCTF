namespace GZCTF.Features.SkillTrees.Domain;

public static class SkillTreeIconCatalog
{
    public const string Default = "flag";

    public static IReadOnlyDictionary<string, string> Values { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["flag"] = "🚩",
            ["web"] = "🕸️",
            ["crypto"] = "🔐",
            ["pwn"] = "💣",
            ["brain"] = "🧠",
            ["ai"] = "🤖",
            ["terminal"] = "💻",
            ["network"] = "🌐",
            ["cloud"] = "☁️",
            ["shield"] = "🛡️",
            ["bug"] = "🐛",
            ["code"] = "🧩",
            ["database"] = "🗄️",
            ["linux"] = "🐧",
            ["windows"] = "🪟",
            ["mobile"] = "📱",
            ["reverse"] = "⚙️",
            ["forensics"] = "🔎",
            ["osint"] = "🛰️",
            ["binary"] = "🔢",
            ["key"] = "🔑",
            ["lock"] = "🔒",
            ["file"] = "📄",
            ["folder"] = "📂",
            ["book"] = "📚",
            ["target"] = "🎯",
            ["trophy"] = "🏆",
            ["rocket"] = "🚀",
            ["lab"] = "🧪",
            ["tools"] = "🛠️",
            ["chip"] = "🔌",
            ["wifi"] = "📡",
            ["globe"] = "🌍",
            ["server"] = "🖥️",
            ["container"] = "📦",
            ["chain"] = "🔗",
            ["puzzle"] = "🧠",
            ["lightning"] = "⚡",
            ["star"] = "⭐",
            ["compass"] = "🧭"
        };

    public static bool IsSupported(string? key) =>
        key is not null && Values.ContainsKey(key);
}
