using GZCTF.Features.SkillTrees.Application;
using System.Net.Mime;
using GZCTF.Features.Shared;
using GZCTF.Features.ChallengeRuntime.Application;
using GZCTF.Features.ChallengeRuntime.Domain;
using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.ChallengeRuntime.Api;

[RequireUser]
[ApiController]
[Route("api/challenges/{challengeId:guid}/instances")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class ChallengeInstancesController(
    ChallengeRuntimeService runtime,
    UserManager<UserInfo> users,
    ChallengeAccessPolicy access,
    AppDbContext db,
    CourseWorkspaceContentService workspaceContents) : ControllerBase
{
    [HttpGet]
    public Task<ActionResult<ChallengeInstanceResponse>> Get(Guid challengeId, CancellationToken token) =>
        Execute(challengeId, () => runtime.GetCurrentInstanceAsync(GetUserId(), challengeId, token), allowOwned: true);

    [HttpPost]
    [AuditAction("challenge_instances.start")]
    public async Task<ActionResult<ChallengeInstanceResponse>> Start(Guid challengeId, [FromQuery] Guid? course, [FromQuery] Guid? testSessionId, CancellationToken token)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var admin = user.Role == GZCTF.Utils.Role.Admin;
        if ((course.HasValue || testSessionId.HasValue) && !admin) return Forbid();
        var draft = course is { } treeId
            ? await workspaceContents.GetWorkspaceChallengeAsync(treeId, challengeId, token) : null;
        if (course.HasValue && draft is null) return NotFound();
        return await Execute(challengeId, () => runtime.StartAsync(user.Id, challengeId, token, admin, draft, testSessionId));
    }

    [HttpPost("test-heartbeat")]
    [RequireAdmin]
    [AuditAction("challenge_instances.test_heartbeat")]
    public Task<ActionResult<ChallengeInstanceResponse>> HeartbeatTest(Guid challengeId,
        [FromBody] TestSessionRequest request, CancellationToken token) =>
        Execute(challengeId, () => runtime.HeartbeatTestAsync(GetUserId(), challengeId,
            request.InstanceId, request.TestSessionId, token), allowOwned: true);

    [HttpPost("test-stop")]
    [RequireAdmin]
    [AuditAction("challenge_instances.test_stop")]
    public async Task<IActionResult> StopTest(Guid challengeId, [FromBody] TestSessionRequest request, CancellationToken token)
    {
        try
        {
            await runtime.StopTestAsync(GetUserId(), challengeId, request.InstanceId, request.TestSessionId, token);
            return NoContent();
        }
        catch (ChallengeRuntimeException error)
        {
            return StatusCode(error.StatusCode,
                new ApiError(error.Code, error.UserMessage, HttpContext.TraceIdentifier));
        }
    }

    [HttpPost("extend")]
    [AuditAction("challenge_instances.extend")]
    public Task<ActionResult<ChallengeInstanceResponse>> Extend(Guid challengeId, CancellationToken token) =>
        Execute(challengeId, () => runtime.ExtendAsync(GetUserId(), challengeId, token), allowOwned: true);

    [HttpDelete]
    [AuditAction("challenge_instances.stop")]
    public async Task<IActionResult> Stop(Guid challengeId, CancellationToken token)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (!await access.CanAccessAsync(challengeId, user, token) &&
            !await db.UserChallengeInstances.AnyAsync(i => i.UserId == user.Id && i.ChallengeId == challengeId && i.IsActive, token)) return NotFound();
        try
        {
            await runtime.StopAsync(GetUserId(), challengeId, token);
            return NoContent();
        }
        catch (ChallengeRuntimeException error)
        {
            return StatusCode(error.StatusCode,
                new ApiError(error.Code, error.UserMessage, HttpContext.TraceIdentifier));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    private async Task<ActionResult<ChallengeInstanceResponse>> Execute(
        Guid challengeId, Func<Task<UserChallengeInstance>> action, bool allowOwned = false)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (!await access.CanAccessAsync(challengeId, user, HttpContext.RequestAborted) &&
            !(allowOwned && await db.UserChallengeInstances.AnyAsync(i => i.UserId == user.Id && i.ChallengeId == challengeId && i.IsActive, HttpContext.RequestAborted))) return NotFound();
        try
        {
            return Ok(await ToResponseAsync(await action(), HttpContext.RequestAborted));
        }
        catch (ChallengeInstanceLimitException)
        {
            return Conflict(new { code = "challenge.instance_limit_reached" });
        }
        catch (ChallengeRuntimeException error)
        {
            return StatusCode(error.StatusCode,
                new ApiError(error.Code, error.UserMessage, HttpContext.TraceIdentifier));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    private Guid GetUserId() => Guid.TryParse(users.GetUserId(User), out var id)
        ? id
        : throw new UnauthorizedAccessException();

    private async Task<ChallengeInstanceResponse> ToResponseAsync(
        UserChallengeInstance instance, CancellationToken token)
    {
        var container = instance.ContainerId is { } containerId
            ? await db.Containers.AsNoTracking().FirstOrDefaultAsync(item => item.Id == containerId, token)
            : null;
        var host = container is null ? null : container.IsProxy
            ? container.Entry : container.PublicIP ?? container.IP;
        int? port = container is null || container.IsProxy
            ? null : container.PublicPort ?? container.Port;
        return new ChallengeInstanceResponse(
            instance.Id, instance.Status, instance.StartedAtUtc, instance.ExpiresAtUtc,
            host, port, instance.AssignedAttachmentKey, instance.AssignedAttachmentSha256);
    }
}

public sealed record TestSessionRequest(Guid InstanceId, Guid TestSessionId);
