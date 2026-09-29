using System.Net.Mime;
using GZCTF.Features.ChallengeRuntime.Application;
using GZCTF.Features.ChallengeRuntime.Domain;
using GZCTF.Middlewares;
using GZCTF.Models.Data;
using GZCTF.Utils;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GZCTF.Features.ChallengeRuntime.Api;

[ApiController]
[Route("api/challenges/{challengeId:guid}/community-writeups")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class CommunityWriteupsController(
    CommunityWriteupService writeups, UserManager<UserInfo> users) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CommunityWriteupSummary>>> List(
        Guid challengeId, CancellationToken token)
    {
        var items = await writeups.ListApprovedAsync(challengeId, token);
        return items is null ? NotFound() : Ok(items);
    }

    [HttpPost]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.CommunityWriteup))]
    [RequestSizeLimit(CommunityWriteupService.MaxPdfBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = CommunityWriteupService.MaxPdfBytes + 1024 * 1024)]
    public async Task<IActionResult> Submit(Guid challengeId, [FromForm] string? title,
        [FromForm] string? authorName, [FromForm] IFormFile? file, CancellationToken token)
    {
        try
        {
            var id = await writeups.SubmitAsync(challengeId, title, authorName, file, token);
            return id is null ? NotFound() : StatusCode(StatusCodes.Status201Created, new { id });
        }
        catch (ArgumentException error)
        {
            return BadRequest(new { message = error.Message });
        }
    }

    [HttpGet("{writeupId:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid challengeId, Guid writeupId, CancellationToken token)
    {
        var user = await users.GetUserAsync(User);
        var stream = await writeups.OpenPdfAsync(challengeId, writeupId, user?.Role == Role.Admin, token);
        if (stream is null) return NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentDisposition = $"inline; filename=\"{writeupId:N}.pdf\"";
        return File(stream, MediaTypeNames.Application.Pdf, enableRangeProcessing: true);
    }
}

[RequireAdmin]
[ApiController]
[Route("api/admin/community-writeups")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class AdminCommunityWriteupsController(
    CommunityWriteupService writeups, UserManager<UserInfo> users) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminCommunityWriteupSummary>>> List(
        [FromQuery] string? status, CancellationToken token)
    {
        CommunityWriteupStatus? filter = null;
        if (status is not null)
        {
            if (!Enum.TryParse<CommunityWriteupStatus>(status, true, out var parsed) ||
                !Enum.IsDefined(parsed)) return BadRequest();
            filter = parsed;
        }
        return Ok(await writeups.ListForReviewAsync(filter, token));
    }

    [HttpPost("{id:guid}/review")]
    public async Task<IActionResult> Review(Guid id, [FromBody] ReviewCommunityWriteupRequest request,
        CancellationToken token)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        return await writeups.ReviewAsync(id, request.Approved, user.Id, token)
            ? NoContent() : NotFound();
    }
}

public sealed record ReviewCommunityWriteupRequest(bool Approved);
