namespace GZCTF.Features.Auditing.Domain;

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ActorId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string ActorKind { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public string? TargetName { get; set; }
    public bool Succeeded { get; set; }
    public int HttpStatus { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorReason { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public int? AffectedCount { get; set; }
}
