using System.Net.Mime;
using GZCTF.Features.ChallengeRuntime.Application;
using GZCTF.Features.ChallengeRuntime.Domain;
using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.ChallengeRuntime.Api;

public sealed record AdminChallengeInstanceResponse(
    Guid Id, Guid UserId, string UserName, Guid ChallengeId, string ChallengeTitle,
    ChallengeInstanceStatus Status, DateTimeOffset? StartedAtUtc, DateTimeOffset? ExpiresAtUtc,
    Guid? ContainerId, string? PublicIp, int? PublicPort);

[RequireAdmin]
[ApiController]
[Route("api/admin/challenge-instances")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class AdminChallengeInstancesController(
    AppDbContext db, ChallengeRuntimeService runtime,
    LearningInstanceSettingsService settingsService) : ControllerBase
{
    [HttpGet("settings")]
    public Task<LearningInstanceSettings> GetSettings(CancellationToken token) => settingsService.GetAsync(token);

    [HttpPut("settings")]
    [AuditAction("challenge_instances.settings.update")]
    public async Task<IActionResult> SetSettings([FromBody] LearningInstanceSettings settings,
        CancellationToken token)
    {
        if (!settings.IsValid) return BadRequest();
        await settingsService.SaveAsync(settings, token);
        return Ok(settings);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminChallengeInstanceResponse>>> List(CancellationToken token)
    {
        var instances = await db.UserChallengeInstances.AsNoTracking()
            .Include(item => item.User)
            .Include(item => item.Challenge.Localizations)
            .Where(item => item.IsActive && item.Status == ChallengeInstanceStatus.Running &&
                           item.ContainerId != null && item.ExpiresAtUtc > DateTimeOffset.UtcNow &&
                           db.Containers.Any(container => container.Id == item.ContainerId &&
                               container.Status == ContainerStatus.Running &&
                               container.ExpectStopAt > DateTimeOffset.UtcNow))
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToArrayAsync(token);
        var containerIds = instances.Where(item => item.ContainerId.HasValue)
            .Select(item => item.ContainerId!.Value).ToArray();
        var containers = await db.Containers.AsNoTracking()
            .Where(item => containerIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, token);
        return Ok(instances.Select(item =>
        {
            var container = item.ContainerId is { } id && containers.TryGetValue(id, out var found)
                ? found : null;
            return new AdminChallengeInstanceResponse(
                item.Id, item.UserId, item.User.UserName ?? item.UserId.ToString(),
                item.ChallengeId,
                item.Challenge.Localizations.FirstOrDefault(text => text.Locale == "en")?.Title ??
                item.Challenge.Localizations.FirstOrDefault()?.Title ?? item.ChallengeId.ToString(),
                item.Status, item.StartedAtUtc, item.ExpiresAtUtc, item.ContainerId,
                container?.IsProxy == true ? container.Entry : container?.PublicIP ?? container?.IP,
                container is null || container.IsProxy ? null : container.PublicPort ?? container.Port);
        }).ToArray());
    }

    [HttpDelete("{id:guid}")]
    [AuditAction("challenge_instances.admin_stop")]
    public async Task<IActionResult> Stop(Guid id, CancellationToken token)
    {
        var instance = await db.UserChallengeInstances.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id && item.IsActive, token);
        if (instance is null) return NotFound();
        try
        {
            await runtime.StopAsync(instance.UserId, instance.ChallengeId, token);
            return NoContent();
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }
}
