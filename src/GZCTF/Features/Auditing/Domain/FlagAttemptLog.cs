namespace GZCTF.Features.Auditing.Domain;

public sealed class FlagAttemptLog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public Guid ChallengeId { get; set; }
    public string? ChallengeName { get; set; }
    public Guid? SubmissionId { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? RejectionCode { get; set; }
    public string? ProtectedSubmittedFlag { get; set; }
}
