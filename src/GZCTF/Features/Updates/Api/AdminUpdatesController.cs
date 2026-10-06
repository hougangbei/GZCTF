using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using GZCTF.Features.Auditing.Application;
using GZCTF.Features.Updates.Application;
using GZCTF.Middlewares;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Features.Updates.Api;

[RequireAdmin]
[ApiController]
[Route("api/admin/updates")]
public sealed class AdminUpdatesController(UpdateAgentClient updater,
    ILogger<AdminUpdatesController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Status([FromQuery] bool refresh, CancellationToken token)
    {
        if (!updater.Configured)
            return Ok(new { configured = false, phase = "disabled",
                message = "此部署尚未连接宿主机更新程序。" });
        return await ForwardAsync(HttpMethod.Get, refresh ? "/status?refresh=1" : "/status",
            null, token);
    }

    [HttpPost]
    [AuditAction("updates.apply")]
    public async Task<IActionResult> Apply([FromBody] ApplyUpdateRequest request,
        CancellationToken token)
    {
        if (!updater.Configured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "此部署尚未连接宿主机更新程序。" });
        if (request.TargetSha is null ||
            !Regex.IsMatch(request.TargetSha, "\\A[0-9a-f]{40}\\z", RegexOptions.CultureInvariant))
            return BadRequest(new { message = "目标版本无效。" });
        return await ForwardAsync(HttpMethod.Post, "/apply",
            new { targetSha = request.TargetSha }, token);
    }

    private async Task<IActionResult> ForwardAsync(HttpMethod method, string path,
        object? body, CancellationToken token)
    {
        try
        {
            var result = await updater.SendAsync(method, path, body, token);
            Response.Headers.CacheControl = "no-store";
            return StatusCode(result.StatusCode, result.Payload);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidOperationException or HttpRequestException or SocketException or JsonException or
            TaskCanceledException)
        {
            logger.LogWarning("Update agent unavailable ({ErrorType})", error.GetType().Name);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "更新程序暂不可用，请检查部署机上的更新服务。" });
        }
    }
}

public sealed record ApplyUpdateRequest(string? TargetSha);
