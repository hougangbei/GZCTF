using GZCTF.Features.Auditing.Application;
using GZCTF.Features.Auditing.Domain;
using GZCTF.Middlewares;
using GZCTF.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.Auditing.Api;

[RequireAdmin]
[ApiController]
[Route("api/admin/audit-events")]
public sealed class AdminAuditController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditEventSummary>>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? category = null, [FromQuery] bool? succeeded = null,
        [FromQuery] string? search = null, CancellationToken token = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            category?.Length > 32 || search?.Length > 120)
            return BadRequest();

        IQueryable<AuditEvent> query = db.AuditEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(item => item.Category == category);
        if (succeeded.HasValue) query = query.Where(item => item.Succeeded == succeeded.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var idMatches = Guid.TryParse(term, out var actorId) ? actorId : (Guid?)null;
            query = query.Where(item => item.ActorName.Contains(term) ||
                (item.TargetName != null && item.TargetName.Contains(term)) ||
                (item.TargetId != null && item.TargetId.Contains(term)) ||
                (item.ActorId.HasValue && (idMatches.HasValue && item.ActorId == idMatches ||
                                           item.ActorId.Value.ToString().Contains(term))));
        }

        var total = await query.CountAsync(token);
        var items = await query.OrderByDescending(item => item.OccurredAtUtc).ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new AuditEventSummary(item.Id, item.OccurredAtUtc, item.ActorId, item.ActorName,
                item.ActorKind, item.Category, item.Action, item.TargetType, item.TargetId, item.TargetName,
                item.Succeeded, item.HttpStatus, item.ErrorCode, item.ErrorReason, item.RequestId, item.AffectedCount))
            .ToArrayAsync(token);
        return Ok(new PagedResult<AuditEventSummary>(items, total, page, pageSize));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AuditEventSummary>> Detail(Guid id, CancellationToken token)
    {
        var item = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, token);
        return item is null ? NotFound() : Ok(AuditEventSummary.From(item));
    }
}

public sealed record AuditEventSummary(Guid Id, DateTimeOffset OccurredAtUtc, Guid? ActorId, string ActorName,
    string ActorKind, string Category, string Action, string TargetType, string? TargetId, string? TargetName,
    bool Succeeded, int HttpStatus, string? ErrorCode, string? ErrorReason, string RequestId, int? AffectedCount)
{
    public static AuditEventSummary From(AuditEvent item) => new(item.Id, item.OccurredAtUtc, item.ActorId,
        item.ActorName, item.ActorKind, item.Category, item.Action, item.TargetType, item.TargetId,
        item.TargetName, item.Succeeded, item.HttpStatus, item.ErrorCode, item.ErrorReason, item.RequestId,
        item.AffectedCount);
}
