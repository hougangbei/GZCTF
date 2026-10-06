using System.Security.Cryptography;
using GZCTF.Features.Auditing.Application;
using GZCTF.Features.Auditing.Domain;
using GZCTF.Middlewares;
using GZCTF.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.Auditing.Api;

[RequireAdmin]
[ApiController]
[Route("api/admin/flag-attempts")]
public sealed class AdminFlagAttemptsController(
    AppDbContext db,
    IDataProtectionProvider protection,
    ILogger<AdminFlagAttemptsController> logger) : ControllerBase
{
    private readonly IDataProtector _protector = protection.CreateProtector("GZCTF.Auditing.FlagAttempt.v1");

    [HttpGet]
    public async Task<ActionResult<PagedResult<FlagAttemptSummary>>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? outcome = null, [FromQuery] string? search = null,
        CancellationToken token = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            outcome?.Length > 24 || search?.Length > 120)
            return BadRequest();

        IQueryable<FlagAttemptLog> query = db.FlagAttemptLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(outcome)) query = query.Where(item => item.Outcome == outcome);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var userId = Guid.TryParse(term, out var parsed) ? parsed : (Guid?)null;
            query = query.Where(item => item.UserName.Contains(term) ||
                (item.ChallengeName != null && item.ChallengeName.Contains(term)) ||
                (userId.HasValue && item.UserId == userId) || item.UserId.ToString().Contains(term) ||
                item.ChallengeId.ToString().Contains(term));
        }

        var total = await query.CountAsync(token);
        var items = await query.OrderByDescending(item => item.OccurredAtUtc).ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new FlagAttemptSummary(item.Id, item.OccurredAtUtc, item.UserId, item.UserName,
                item.ChallengeId, item.ChallengeName, item.SubmissionId, item.Outcome, item.RejectionCode))
            .ToArrayAsync(token);
        return Ok(new PagedResult<FlagAttemptSummary>(items, total, page, pageSize));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FlagAttemptDetail>> Detail(Guid id, CancellationToken token)
    {
        var item = await db.FlagAttemptLogs.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, token);
        if (item is null) return NotFound();

        var available = item.ProtectedSubmittedFlag is not null &&
                        item.OccurredAtUtc.AddDays(30) > DateTimeOffset.UtcNow;
        string? submittedFlag = null;
        if (available)
        {
            try
            {
                submittedFlag = _protector.Unprotect(item.ProtectedSubmittedFlag!);
            }
            catch (CryptographicException error)
            {
                logger.LogWarning("Could not decrypt Flag attempt detail {AttemptId}; error type {ErrorType}",
                    item.Id, error.GetType().Name);
                available = false;
            }
        }

        return Ok(new FlagAttemptDetail(item.Id, item.OccurredAtUtc, item.UserId, item.UserName,
            item.ChallengeId, item.ChallengeName, item.SubmissionId, item.Outcome, item.RejectionCode,
            available, submittedFlag));
    }
}

public sealed record FlagAttemptSummary(Guid Id, DateTimeOffset OccurredAtUtc, Guid UserId, string UserName,
    Guid ChallengeId, string? ChallengeName, Guid? SubmissionId, string Outcome, string? RejectionCode);

public sealed record FlagAttemptDetail(Guid Id, DateTimeOffset OccurredAtUtc, Guid UserId, string UserName,
    Guid ChallengeId, string? ChallengeName, Guid? SubmissionId, string Outcome, string? RejectionCode,
    bool OriginalAvailable, string? SubmittedFlag);
