using GZCTF.Features.Auditing.Application;
using GZCTF.Middlewares;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.Accounts.Api;

public sealed record RegistrationReviewItem(
    Guid Id, string UserName, string Email, string RealName, string StdNumber,
    bool EmailConfirmed, string Status, DateTimeOffset RegisterTimeUtc);

public sealed record RegistrationReviewPage(int Total, IReadOnlyList<RegistrationReviewItem> Items);

[RequireAdmin]
[ApiController]
[Route("api/admin/registration-reviews")]
public sealed class AdminRegistrationReviewsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RegistrationReviewPage>> List(
        [FromQuery] int offset = 0, [FromQuery] int limit = 50, CancellationToken token = default)
    {
        if (offset < 0 || limit is < 1 or > 100) return BadRequest();
        var query = db.Users.AsNoTracking()
            .Where(user => user.ApprovalStatus != RegistrationApprovalStatus.Approved);
        var total = await query.CountAsync(token);
        var users = await query.OrderBy(user => user.RegisterTimeUtc).ThenBy(user => user.Id)
            .Skip(offset).Take(limit).ToArrayAsync(token);
        return Ok(new RegistrationReviewPage(total, users.Select(user => new RegistrationReviewItem(
            user.Id, user.UserName ?? string.Empty, user.Email ?? string.Empty,
            user.RealName, user.StdNumber, user.EmailConfirmed,
            user.ApprovalStatus.ToString(), user.RegisterTimeUtc)).ToArray()));
    }

    [HttpPost("{id:guid}/approve")]
    [AuditAction("users.registration.approve", TargetIdParameter = "id")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken token)
    {
        var changed = await db.Users.Where(user => user.Id == id && user.Role == Role.User &&
                user.EmailConfirmed && user.ApprovalStatus != RegistrationApprovalStatus.Approved &&
                user.RealName != string.Empty && user.StdNumber != string.Empty)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.ApprovalStatus,
                RegistrationApprovalStatus.Approved), token);
        return changed == 1 ? NoContent() : Conflict();
    }

    [HttpPost("{id:guid}/reject")]
    [AuditAction("users.registration.reject", TargetIdParameter = "id")]
    public async Task<IActionResult> Reject(Guid id, CancellationToken token)
    {
        var changed = await db.Users.Where(user => user.Id == id && user.Role == Role.User &&
                user.EmailConfirmed && user.ApprovalStatus == RegistrationApprovalStatus.Pending)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.ApprovalStatus,
                RegistrationApprovalStatus.Rejected), token);
        return changed == 1 ? NoContent() : Conflict();
    }
}
