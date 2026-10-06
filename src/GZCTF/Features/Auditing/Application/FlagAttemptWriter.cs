using GZCTF.Features.Auditing.Domain;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.Auditing.Application;

public sealed class FlagAttemptWriter(
    AppDbContext db,
    IDataProtectionProvider protection,
    ILogger<FlagAttemptWriter> logger)
{
    private readonly IDataProtector _protector = protection.CreateProtector("GZCTF.Auditing.FlagAttempt.v1");

    public async Task<Guid?> BeginAsync(UserInfo user, Guid challengeId, string value, CancellationToken token)
    {
        FlagAttemptLog? attempted = null;
        try
        {
            var challenge = await db.Challenges.AsNoTracking().Where(item => item.Id == challengeId)
                .Select(item => new
                {
                    item.Id,
                    Name = item.Localizations.OrderBy(locale => locale.Locale == "zh-CN" ? 0 : 1)
                        .Select(locale => locale.Title).FirstOrDefault()
                }).SingleOrDefaultAsync(token);
            attempted = new FlagAttemptLog
            {
                UserId = user.Id,
                UserName = (user.UserName ?? "unknown")[..Math.Min(user.UserName?.Length ?? 7, 80)],
                ChallengeId = challenge?.Id ?? challengeId,
                ChallengeName = challenge?.Name is { Length: > 160 } name ? name[..160] : challenge?.Name,
                Outcome = "pending",
                ProtectedSubmittedFlag = _protector.Protect(value)
            };
            db.FlagAttemptLogs.Add(attempted);
            await db.SaveChangesAsync(token);
            return attempted.Id;
        }
        catch (Exception error)
        {
            if (attempted is not null)
                db.Entry(attempted).State = EntityState.Detached;
            logger.LogError("Flag attempt audit persistence failed at request start; error type {ErrorType}",
                error.GetType().Name);
            return null;
        }
    }

    public async Task CompleteAsync(Guid? id, string outcome, string? rejectionCode, Guid? submissionId,
        CancellationToken token)
    {
        if (id is null) return;
        try
        {
            var entry = await db.FlagAttemptLogs.SingleOrDefaultAsync(item => item.Id == id, token);
            if (entry is null) return;
            entry.Outcome = outcome;
            entry.RejectionCode = rejectionCode is { Length: > 48 } ? rejectionCode[..48] : rejectionCode;
            entry.SubmissionId = submissionId;
            await db.SaveChangesAsync(token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError("Flag attempt audit finalization failed for event {EventId}; error type {ErrorType}",
                id, error.GetType().Name);
        }
    }
}
