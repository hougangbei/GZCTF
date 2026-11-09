using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using GZCTF.Features.Auditing.Application;
using GZCTF.Features.Updates.Application;
using GZCTF.Middlewares;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Features.Updates.Api;

[RequireAdmin]
[ApiController]
[Route("api/admin/updates/packages")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminUpdatePackagesController(UpdateAgentClient updater,
    ILogger<AdminUpdatePackagesController> logger) : ControllerBase
{
    public const long MaxUploadBytes = 1024L * 1024 * 1024;
    private string? Actor => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private static bool Id(string? value) => value is not null &&
        Regex.IsMatch(value, "\\A[0-9a-f]{32}\\z", RegexOptions.CultureInvariant);

    [HttpGet("settings")]
    public Task<IActionResult> Settings(CancellationToken token) => Forward(HttpMethod.Get, "/packages/settings", null, token);

    [HttpPut("settings")]
    [AuditAction("updates.packages.directory")]
    public Task<IActionResult> SaveSettings([FromBody] PackageDirectoryRequest request, CancellationToken token) =>
        string.IsNullOrWhiteSpace(request.Directory) || request.Directory.Length > 2048 ||
        !request.Directory.StartsWith('/') || request.Directory.Any(char.IsControl)
            ? Task.FromResult<IActionResult>(BadRequest(new { message = "请输入有效的服务器绝对目录。" }))
            : Forward(HttpMethod.Put, "/packages/settings", new { directory = request.Directory }, token);

    [HttpGet]
    public Task<IActionResult> List(CancellationToken token) => Forward(HttpMethod.Get, "/packages", null, token);

    [HttpPost("upload")]
    [RequestSizeLimit(MaxUploadBytes)]
    [AuditAction("updates.packages.upload")]
    public async Task<IActionResult> Upload(CancellationToken token)
    {
        if (Request.ContentLength is null or <= 0)
            return BadRequest(new { message = "更新包为空或缺少文件长度。" });
        if (Request.ContentLength > MaxUploadBytes)
            return StatusCode(413, new { message = "更新包超过1GiB限制。" });
        var name = Uri.UnescapeDataString(Request.Headers["X-File-Name"].ToString());
        if (name.Length is 0 or > 180 || name.Any(char.IsControl) || name.IndexOfAny(['/', '\\', ':']) >= 0 ||
            !Regex.IsMatch(name, @"\.(tar\.gz|tgz|tar|zip)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return BadRequest(new { message = "请选择有效的离线更新包文件。" });
        return await Execute(() => updater.UploadAsync(Request.Body, Request.ContentLength.Value, name, token, Actor));
    }

    [HttpPost("{id}/checksum")]
    [AuditAction("updates.packages.checksum")]
    public Task<IActionResult> Checksum(string id, [FromBody] PackageChecksumRequest request, CancellationToken token) =>
        !Id(id) || request.Sha256 is null || !Regex.IsMatch(request.Sha256, "\\A[0-9a-fA-F]{64}\\z")
            ? Task.FromResult<IActionResult>(BadRequest(new { message = "包标识或SHA-256校验值无效。" }))
            : Forward(HttpMethod.Post, $"/packages/{id}/checksum", new { sha256 = request.Sha256.ToLowerInvariant() }, token);

    [HttpPost("{id}/validate")]
    [AuditAction("updates.packages.validate")]
    public Task<IActionResult> Validate(string id, CancellationToken token) => !Id(id)
        ? Task.FromResult<IActionResult>(BadRequest(new { message = "更新包标识无效。" }))
        : Forward(HttpMethod.Post, $"/packages/{id}/validate", new { }, token);

    [HttpPost("{id}/apply")]
    [AuditAction("updates.packages.apply")]
    public Task<IActionResult> Apply(string id, [FromBody] PackageApplyRequest request, CancellationToken token) =>
        !Id(id) || !Id(request.ValidationId) || !Guid.TryParse(request.OperationId, out var operation) ||
        operation == Guid.Empty || !request.MaintenanceConfirmed
            ? Task.FromResult<IActionResult>(BadRequest(new { message = "请重新检查更新包并确认维护后更新。" }))
            : Forward(HttpMethod.Post, $"/packages/{id}/apply", new
            {
                validationId = request.ValidationId,
                operationId = operation.ToString("D"),
                maintenanceConfirmed = true
            }, token);

    private Task<IActionResult> Forward(HttpMethod method, string path, object? body, CancellationToken token) =>
        Execute(() => updater.SendAsync(method, path, body, token, Actor));

    private async Task<IActionResult> Execute(Func<Task<UpdateAgentResult>> operation)
    {
        if (!updater.Configured) return StatusCode(503, new { message = "请先按接入说明连接宿主机更新服务。" });
        try
        {
            var result = await operation();
            return StatusCode(result.StatusCode, result.Payload);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or
            HttpRequestException or SocketException or JsonException or TaskCanceledException)
        {
            logger.LogWarning("Package update service unavailable ({ErrorType})", error.GetType().Name);
            return StatusCode(503, new { message = "更新服务暂不可用或传输中断，请检查状态后重试。" });
        }
    }
}

public sealed record PackageDirectoryRequest(string Directory);
public sealed record PackageChecksumRequest(string Sha256);
public sealed record PackageApplyRequest(string ValidationId, string OperationId, bool MaintenanceConfirmed);
