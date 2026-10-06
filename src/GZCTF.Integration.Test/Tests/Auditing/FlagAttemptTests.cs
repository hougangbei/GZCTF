using GZCTF.Features.Auditing.Application;
using GZCTF.Features.Auditing.Domain;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Services.CronJob;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Auditing;

[Collection(nameof(IntegrationTestCollection))]
public sealed class FlagAttemptTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Writer_protects_the_exact_submitted_value_and_links_a_submission()
    {
        const string submitted = "  flag{preserve exact bytes}\t";
        var seeded = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Flag!Attempt1");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(item => item.Id == seeded.Id);
        var writer = scope.ServiceProvider.GetRequiredService<FlagAttemptWriter>();
        var attemptId = await writer.BeginAsync(user, Guid.CreateVersion7(), submitted, CancellationToken.None);

        var pending = await db.FlagAttemptLogs.SingleAsync(item => item.Id == attemptId);
        Assert.Equal("pending", pending.Outcome);
        Assert.NotEqual(submitted, pending.ProtectedSubmittedFlag);
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("GZCTF.Auditing.FlagAttempt.v1");
        Assert.Equal(submitted, protector.Unprotect(pending.ProtectedSubmittedFlag!));

        var submissionId = Guid.CreateVersion7();
        await writer.CompleteAsync(attemptId, "incorrect", "challenge.flag_incorrect", submissionId,
            CancellationToken.None);
        var completed = await db.FlagAttemptLogs.SingleAsync(item => item.Id == attemptId);
        Assert.Equal("incorrect", completed.Outcome);
        Assert.Equal("challenge.flag_incorrect", completed.RejectionCode);
        Assert.Equal(submissionId, completed.SubmissionId);
    }

    [Fact]
    public async Task Daily_cleanup_nulls_only_values_past_thirty_days_and_is_repeatable()
    {
        var expiredId = Guid.CreateVersion7();
        var freshId = Guid.CreateVersion7();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.FlagAttemptLogs.AddRange(
                new FlagAttemptLog
                {
                    Id = expiredId, OccurredAtUtc = DateTimeOffset.UtcNow.AddDays(-31), UserId = Guid.CreateVersion7(),
                    UserName = "kept-user", ChallengeId = Guid.CreateVersion7(), ChallengeName = "kept-challenge",
                    Outcome = "incorrect", RejectionCode = "kept-code", ProtectedSubmittedFlag = "ciphertext-expired"
                },
                new FlagAttemptLog
                {
                    Id = freshId, OccurredAtUtc = DateTimeOffset.UtcNow.AddDays(-29), UserId = Guid.CreateVersion7(),
                    UserName = "fresh-user", ChallengeId = Guid.CreateVersion7(), Outcome = "accepted",
                    ProtectedSubmittedFlag = "ciphertext-fresh"
                });
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<CronJobService>>();
            await RuntimeCronJobs.ClearExpiredFlagAttemptValues(scope, logger);
            await RuntimeCronJobs.ClearExpiredFlagAttemptValues(scope, logger);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var expired = await db.FlagAttemptLogs.SingleAsync(item => item.Id == expiredId);
            var fresh = await db.FlagAttemptLogs.SingleAsync(item => item.Id == freshId);
            Assert.Null(expired.ProtectedSubmittedFlag);
            Assert.Equal("kept-user", expired.UserName);
            Assert.Equal("kept-challenge", expired.ChallengeName);
            Assert.Equal("incorrect", expired.Outcome);
            Assert.Equal("kept-code", expired.RejectionCode);
            Assert.Equal("ciphertext-fresh", fresh.ProtectedSubmittedFlag);
        }
    }
}
