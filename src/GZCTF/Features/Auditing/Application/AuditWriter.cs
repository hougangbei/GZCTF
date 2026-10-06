using System.Security.Claims;
using GZCTF.Features.Auditing.Domain;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.Auditing.Application;

public sealed class AuditWriter(
    IServiceScopeFactory scopeFactory,
    ILogger<AuditWriter> logger)
{
    public async Task WriteAsync(HttpContext context, AuditActionDefinition action, int status,
        string? targetId = null, string? targetName = null, int? affectedCount = null,
        CancellationToken cancellationToken = default)
    {
        var actorId = Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : (Guid?)null;

        var succeeded = status is >= 200 and < 400;
        var (errorCode, errorReason) = succeeded ? (null, null) : status switch
        {
            400 => ("validation_failed", "The request did not pass validation."),
            401 => ("unauthenticated", "Authentication is required."),
            403 => ("forbidden", "The actor is not allowed to perform this action."),
            404 => ("not_found", "The target could not be found."),
            409 => ("conflict", "The action conflicts with the current state."),
            _ => ("server_error", "The action could not be completed.")
        };

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actor = actorId is { } actorKey
                ? await db.Users.AsNoTracking().Where(user => user.Id == actorKey)
                    .Select(user => new { user.UserName, user.Role }).SingleOrDefaultAsync(cancellationToken)
                : null;
            var actorName = actor?.UserName ?? "unknown";
            db.AuditEvents.Add(new AuditEvent
            {
                OccurredAtUtc = DateTimeOffset.UtcNow,
                ActorId = actorId,
                ActorName = actorName.Length <= 80 ? actorName : actorName[..80],
                ActorKind = actor is null ? "unknown" : actor.Role >= Role.Admin ? "admin" : "user",
                Category = action.Category,
                Action = action.Code,
                TargetType = action.TargetType,
                TargetId = targetId is { Length: > 128 } ? targetId[..128] : targetId,
                TargetName = targetName is { Length: > 160 } ? targetName[..160] : targetName,
                Succeeded = succeeded,
                HttpStatus = status,
                ErrorCode = errorCode,
                ErrorReason = errorReason,
                RequestId = context.TraceIdentifier.Length <= 64 ? context.TraceIdentifier : context.TraceIdentifier[^64..],
                AffectedCount = affectedCount
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception error)
        {
            logger.LogError("Audit event persistence failed for {ActionCode}; error type {ErrorType}",
                action.Code, error.GetType().Name);
        }
    }
}
