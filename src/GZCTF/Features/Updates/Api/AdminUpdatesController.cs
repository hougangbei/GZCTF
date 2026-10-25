using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using GZCTF.Features.Auditing.Application;
using GZCTF.Features.Updates.Application;
using GZCTF.Middlewares;
using GZCTF.Services.Config;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Features.Updates.Api;

[RequireAdmin]
[ApiController]
[Route("api/admin/updates")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminUpdatesController(UpdateAgentClient updater,
    ILogger<AdminUpdatesController> logger, IConfiguration configuration, IConfigService configService) : ControllerBase
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

    [HttpGet("source")]
    public IActionResult Source() => Ok(SavedSource());

    [HttpPut("source")]
    [AuditAction("updates.source.save")]
    public async Task<IActionResult> SaveSource([FromBody] UpdateSourceSettings? request, CancellationToken token)
    {
        // Generations are owned by the server, including when the client sends a stale value.
        if (request is not null) request.Generation = "";
        if (!UpdateSourceSettings.TryNormalize(request, out var source))
            return BadRequest(new { message = "更新来源无效，请检查类型、地址、分支及 SHA-256。" });
        source.Generation = Guid.NewGuid().ToString("N");
        await configService.SaveConfig(source, token);
        return Ok(source);
    }

    [HttpPost("validate")]
    [AuditAction("updates.validate")]
    public async Task<IActionResult> Validate(CancellationToken token)
    {
        if (!updater.Configured) return AgentNotConfigured();
        if (!UpdateSourceSettings.TryNormalize(SavedSource(), out var source))
            return BadRequest(new { message = "请先保存有效的更新来源。" });
        return await ForwardAsync(HttpMethod.Post, "/validate", new { source }, token);
    }

    [HttpPost]
    [AuditAction("updates.apply")]
    public async Task<IActionResult> Apply([FromBody] ApplyUpdateRequest? request, CancellationToken token)
    {
        if (request is null) return BadRequest(new { message = "验证记录无效，请先验证更新来源。" });
        if (request.ValidationId is not null)
        {
            if (!Regex.IsMatch(request.ValidationId, "\\A[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
                return BadRequest(new { message = "验证记录无效，请先验证更新来源。" });
            if (!updater.Configured) return AgentNotConfigured();
            if (!UpdateSourceSettings.TryNormalize(SavedSource(), out var source))
                return BadRequest(new { message = "请先保存有效的更新来源。" });
            return await ForwardAsync(HttpMethod.Post, "/apply", new { validationId = request.ValidationId, source }, token);
        }
        if (request.TargetSha is null ||
            !Regex.IsMatch(request.TargetSha, "\\A[0-9a-f]{40}\\z", RegexOptions.CultureInvariant))
            return BadRequest(new { message = "目标版本无效。" });
        if (!updater.Configured) return AgentNotConfigured();
        return await ForwardAsync(HttpMethod.Post, "/apply", new { targetSha = request.TargetSha }, token);
    }

    private UpdateSourceSettings SavedSource() =>
        configuration.GetSection(nameof(UpdateSourceSettings)).Get<UpdateSourceSettings>() ?? new();

    private ObjectResult AgentNotConfigured() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { message = "此部署尚未连接宿主机更新程序。" });

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

public sealed record ApplyUpdateRequest(string? TargetSha = null, string? ValidationId = null);
