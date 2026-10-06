using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.QqBot.Application;

public sealed record QqBotSettings(
    bool Enabled = false,
    string BaseUrl = "",
    string AccessToken = "",
    string GroupId = "",
    string MessageTemplate = "🎉 {member} 解出了 {challenge}（{source}）",
    bool NotifyLearningSolves = true,
    bool NotifyGameSolves = true,
    bool NotifyChallengePublishes = true,
    bool NotifyAnnouncements = true,
    bool NotifyHints = true)
{
    public const string DefaultTemplate = "🎉 {member} 解出了 {challenge}（{source}）";

    public bool IsValid =>
        MessageTemplate is { Length: <= 1000 } && QqMessageTemplate.IsValid(MessageTemplate) &&
        BaseUrl is { Length: <= 500 } && AccessToken is { Length: <= 500 } &&
        GroupId is { Length: <= 30 } &&
        (string.IsNullOrWhiteSpace(BaseUrl) ||
         (Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")) &&
        (!Enabled || (!string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(GroupId)));
}

public sealed record QqBotSettingsView(
    bool Enabled, string BaseUrl, bool AccessTokenConfigured, string GroupId,
    string MessageTemplate, bool NotifyLearningSolves, bool NotifyGameSolves,
    bool NotifyChallengePublishes, bool NotifyAnnouncements, bool NotifyHints);

public sealed record QqBotSettingsCommand(
    bool Enabled, string BaseUrl, string? AccessToken, bool ClearAccessToken,
    string GroupId, string MessageTemplate, bool NotifyLearningSolves, bool NotifyGameSolves,
    bool NotifyChallengePublishes = true, bool NotifyAnnouncements = true, bool NotifyHints = true);

public sealed class QqBotSettingsService(AppDbContext db)
{
    private const string Key = "QqBotSettings";

    public async Task<QqBotSettings> GetAsync(CancellationToken token = default)
    {
        var raw = await db.Configs.AsNoTracking().Where(item => item.ConfigKey == Key)
            .Select(item => item.Value).SingleOrDefaultAsync(token);
        if (string.IsNullOrWhiteSpace(raw)) return new QqBotSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<QqBotSettings>(raw);
            return settings is { IsValid: true } ? settings : new QqBotSettings();
        }
        catch (JsonException)
        {
            return new QqBotSettings();
        }
    }

    public async Task<QqBotSettings?> SaveAsync(QqBotSettingsCommand command, CancellationToken token = default)
    {
        if (command.BaseUrl is null || command.GroupId is null || command.MessageTemplate is null)
            return null;
        var current = await GetAsync(token);
        var settings = new QqBotSettings(
            command.Enabled, command.BaseUrl.Trim().TrimEnd('/'),
            command.ClearAccessToken ? "" : command.AccessToken ?? current.AccessToken,
            command.GroupId.Trim(), command.MessageTemplate,
            command.NotifyLearningSolves, command.NotifyGameSolves,
            command.NotifyChallengePublishes, command.NotifyAnnouncements, command.NotifyHints);
        if (!settings.IsValid) return null;
        var row = await db.Configs.SingleOrDefaultAsync(item => item.ConfigKey == Key, token);
        if (row is null) db.Configs.Add(new Config(Key, JsonSerializer.Serialize(settings)));
        else row.Value = JsonSerializer.Serialize(settings);
        await db.SaveChangesAsync(token);
        return settings;
    }

    public static QqBotSettingsView ToView(QqBotSettings settings) => new(
        settings.Enabled, settings.BaseUrl, !string.IsNullOrWhiteSpace(settings.AccessToken),
        settings.GroupId, settings.MessageTemplate,
        settings.NotifyLearningSolves, settings.NotifyGameSolves,
        settings.NotifyChallengePublishes, settings.NotifyAnnouncements, settings.NotifyHints);
}
