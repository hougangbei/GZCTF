using System.Net.Mime;
using GZCTF.Features.Auditing.Application;
using GZCTF.Features.ChallengeRuntime.Application;
using GZCTF.Middlewares;
using GZCTF.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.ChallengeRuntime.Api;

[RequireUser]
[ApiController]
[Route("api/challenges/{challengeId:guid}/submissions")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class ChallengeSubmissionsController(
    ChallengeSubmissionService submissions,
    FlagAttemptWriter flagAttempts,
    AppDbContext db,
    UserManager<UserInfo> users,
    ChallengeAccessPolicy access) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ChallengeSubmissionResult>> Submit(
        Guid challengeId, [FromBody] ChallengeSubmissionRequest request, CancellationToken token)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        var submittedFlag = request.Flag ?? string.Empty;
        var attemptId = await flagAttempts.BeginAsync(user, challengeId, submittedFlag, CancellationToken.None);
        if (string.IsNullOrWhiteSpace(submittedFlag))
        {
            await flagAttempts.CompleteAsync(attemptId, "rejected", "empty_flag", null, CancellationToken.None);
            return BadRequest();
        }
        if (!await access.CanAccessAsync(challengeId, user, token))
        {
            await flagAttempts.CompleteAsync(attemptId, "rejected", "challenge_inaccessible", null, CancellationToken.None);
            return NotFound();
        }

        try
        {
            var result = await submissions.SubmitAsync(user.Id, challengeId, submittedFlag, token);
            await flagAttempts.CompleteAsync(attemptId,
                result.Accepted ? "accepted" : result.RejectionCode == "challenge.submission_limit_exhausted" ? "rejected" : "incorrect",
                result.RejectionCode, result.SubmissionId == Guid.Empty ? null : result.SubmissionId, CancellationToken.None);
            return Ok(result);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            await flagAttempts.CompleteAsync(attemptId, "error", "submission_failed", null, CancellationToken.None);
            throw;
        }
        catch (OperationCanceledException)
        {
            await flagAttempts.CompleteAsync(attemptId, "error", "submission_cancelled", null, CancellationToken.None);
            throw;
        }
    }

    [HttpGet]
    public async Task<IActionResult> History(Guid challengeId, CancellationToken token)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        if (!await access.CanAccessAsync(challengeId, user, token)) return NotFound();
        var history = await db.ChallengeSubmissions.AsNoTracking()
            .Where(item => item.UserId == user.Id && item.ChallengeId == challengeId)
            .OrderByDescending(item => item.SubmittedAtUtc)
            .Take(50)
            .Select(item => new
            {
                item.Id,
                item.Accepted,
                item.FirstSolve,
                item.SolveMode,
                item.RejectionCode,
                item.SubmittedAtUtc
            })
            .ToArrayAsync(token);
        return Ok(history);
    }
}

public sealed record ChallengeSubmissionRequest(string Flag);
