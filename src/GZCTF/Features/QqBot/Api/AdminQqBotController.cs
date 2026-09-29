using System.Text.Json;
using GZCTF.Features.QqBot.Application;
using GZCTF.Middlewares;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Features.QqBot.Api;

[RequireAdmin]
[ApiController]
[Route("api/admin/qq-bot")]
public sealed class AdminQqBotController(
    QqBotSettingsService settingsService, QqBotNotifier notifier) : ControllerBase
{
    [HttpGet("settings")]
    public async Task<QqBotSettingsView> GetSettings(CancellationToken token) =>
        QqBotSettingsService.ToView(await settingsService.GetAsync(token));

    [HttpPut("settings")]
    public async Task<ActionResult<QqBotSettingsView>> SetSettings(
        [FromBody] QqBotSettingsCommand command, CancellationToken token)
    {
        var settings = await settingsService.SaveAsync(command, token);
        return settings is null ? BadRequest() : Ok(QqBotSettingsService.ToView(settings));
    }

    [HttpPost("test")]
    public async Task<IActionResult> SendTest(CancellationToken token)
    {
        try
        {
            await notifier.SendTestAsync(token);
            return Ok(new { sent = true });
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or
                                       InvalidOperationException or JsonException)
        {
            return BadRequest(new { sent = false, error = error.Message });
        }
    }
}
