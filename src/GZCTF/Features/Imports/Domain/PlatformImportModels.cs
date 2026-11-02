using System.Text.Json.Serialization;

namespace GZCTF.Features.Imports.Domain;

public sealed class PlatformImportBatch
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string SourceInstance { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string PlanFingerprint { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UserId { get; set; }
    public Guid? TreeId { get; set; }
    public int ChallengeCount { get; set; }
    public int UserCount { get; set; }
    public int TeamCount { get; set; }
    public int SubmissionCount { get; set; }
    public int AwardCount { get; set; }
    public string ReportJson { get; set; } = "{}";
    [JsonIgnore] public string? ProtectedMetadata { get; set; }
}

public sealed class PlatformImportMap
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string SourceInstance { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public Guid BatchId { get; set; }
    [JsonIgnore] public PlatformImportBatch Batch { get; set; } = null!;
}

// Membership is a snapshot at import time; it never follows a user's current native team.
public sealed class PlatformHistoryMember
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BatchId { get; set; }
    [JsonIgnore] public PlatformImportBatch Batch { get; set; } = null!;
    public string SourceUserId { get; set; } = string.Empty;
    public string SourceUserName { get; set; } = string.Empty;
    public string? SourceTeamId { get; set; }
    public string? SourceTeamName { get; set; }
    public Guid? UserId { get; set; }
    [JsonIgnore] public string ProtectedMetadata { get; set; } = string.Empty;
}

public sealed class PlatformHistorySubmission
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BatchId { get; set; }
    [JsonIgnore] public PlatformImportBatch Batch { get; set; } = null!;
    public string SourceId { get; set; } = string.Empty;
    public string? SourceUserId { get; set; }
    public string? SourceUserName { get; set; }
    public string? SourceTeamId { get; set; }
    public string? SourceTeamName { get; set; }
    public string SourceChallengeId { get; set; } = string.Empty;
    public string ChallengeTitle { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public int? Score { get; set; }
    [JsonIgnore] public string ProtectedMetadata { get; set; } = string.Empty;
}

public sealed class PlatformHistoryAward
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BatchId { get; set; }
    [JsonIgnore] public PlatformImportBatch Batch { get; set; } = null!;
    public string SourceId { get; set; } = string.Empty;
    public string? SourceUserId { get; set; }
    public string? SourceUserName { get; set; }
    public string? SourceTeamId { get; set; }
    public string? SourceTeamName { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
    public DateTimeOffset? AwardedAtUtc { get; set; }
    [JsonIgnore] public string ProtectedMetadata { get; set; } = string.Empty;
}
