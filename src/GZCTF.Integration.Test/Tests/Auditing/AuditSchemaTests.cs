using GZCTF.Features.Auditing.Domain;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Auditing;

[Collection(nameof(IntegrationTestCollection))]
public sealed class AuditSchemaTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Audit_and_flag_attempt_records_survive_a_new_scope()
    {
        var auditId = Guid.CreateVersion7();
        var flagAttemptId = Guid.CreateVersion7();

        using (var scope = factory.Services.CreateScope())
        {
            await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditEvents.Add(new AuditEvent
            {
                Id = auditId,
                OccurredAtUtc = DateTimeOffset.UtcNow,
                ActorName = "admin",
                ActorKind = "admin",
                Category = "users",
                Action = "user.update",
                TargetType = "user",
                Succeeded = true,
                HttpStatus = 200,
                RequestId = "request-1"
            });
            db.FlagAttemptLogs.Add(new FlagAttemptLog
            {
                Id = flagAttemptId,
                OccurredAtUtc = DateTimeOffset.UtcNow,
                UserId = Guid.CreateVersion7(),
                UserName = "learner",
                ChallengeId = Guid.CreateVersion7(),
                Outcome = "incorrect",
                ProtectedSubmittedFlag = "protected-value"
            });
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.AuditEvents.AnyAsync(item => item.Id == auditId));
            Assert.True(await db.FlagAttemptLogs.AnyAsync(item => item.Id == flagAttemptId));
        }
    }

    [Fact]
    public void Audit_and_flag_attempts_have_stable_time_id_indexes()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Contains(db.Model.FindEntityType(typeof(AuditEvent))!.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(AuditEvent.OccurredAtUtc), nameof(AuditEvent.Id)]));
        Assert.Contains(db.Model.FindEntityType(typeof(FlagAttemptLog))!.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(FlagAttemptLog.OccurredAtUtc), nameof(FlagAttemptLog.Id)]));
    }
}
