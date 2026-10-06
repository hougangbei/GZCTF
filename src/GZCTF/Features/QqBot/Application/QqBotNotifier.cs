using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace GZCTF.Features.QqBot.Application;

public sealed class QqBotNotifier(
    HttpClient client, QqBotSettingsService settingsService, ILogger<QqBotNotifier> logger)
{
    public Task<bool> TrySendChallengePublishedAsync(string title, CancellationToken token = default) =>
        TrySendBroadcastAsync(settings => settings.NotifyChallengePublishes,
            $"🆕 新题发布：{SafeSubject(title)}", token);

    public Task<bool> TrySendAnnouncementAsync(string title, CancellationToken token = default) =>
        TrySendBroadcastAsync(settings => settings.NotifyAnnouncements,
            $"📢 新公告：{SafeSubject(title)}", token);

    public Task<bool> TrySendHintAddedAsync(string challengeTitle, int count, CancellationToken token = default) =>
        TrySendBroadcastAsync(settings => settings.NotifyHints,
            $"💡 题目「{SafeSubject(challengeTitle)}」新增 {count} 条提示，请到平台查看。", token);

    public async Task<bool> TrySendSolveAsync(QqSolveEvent solve, bool isGame,
        CancellationToken token = default)
    {
        try
        {
            var settings = await settingsService.GetAsync(token);
            if (!settings.Enabled || (isGame ? !settings.NotifyGameSolves : !settings.NotifyLearningSolves))
                return false;
            await SendAsync(settings, QqMessageTemplate.Render(settings.MessageTemplate, solve), token);
            return true;
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            logger.LogWarning(error, "Failed to send QQ solve notification");
            return false;
        }
    }

    private async Task<bool> TrySendBroadcastAsync(
        Func<QqBotSettings, bool> isEnabled, string message, CancellationToken token)
    {
        try
        {
            var settings = await settingsService.GetAsync(token);
            if (!settings.Enabled || !isEnabled(settings)) return false;
            await SendAsync(settings, message, token);
            return true;
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            logger.LogWarning(error, "Failed to send QQ publication notification");
            return false;
        }
    }

    private static string SafeSubject(string value)
    {
        var normalized = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return QqMessageTemplate.EscapeCq(normalized[..Math.Min(normalized.Length, 160)]);
    }

    public async Task SendTestAsync(CancellationToken token = default)
    {
        var settings = await settingsService.GetAsync(token);
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.GroupId))
            throw new InvalidOperationException("NapCat address and group ID are required");
        await SendAsync(settings, "GZCTF QQ 机器人测试消息：连接与群消息发送成功。", token);
    }

    private async Task SendAsync(QqBotSettings settings, string message, CancellationToken token)
    {
        var endpoint = new Uri(new Uri(settings.BaseUrl.TrimEnd('/') + "/"), "send_group_msg");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new { group_id = settings.GroupId, message })
        };
        if (!string.IsNullOrWhiteSpace(settings.AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
        using var response = await client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        using var payload = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
        if (!payload.RootElement.TryGetProperty("status", out var status) ||
            status.GetString() != "ok" ||
            !payload.RootElement.TryGetProperty("retcode", out var code) ||
            !code.TryGetInt32(out var retcode) || retcode != 0)
            throw new InvalidOperationException("NapCat rejected the message");
    }
}
