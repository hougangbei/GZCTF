using GZCTF.Utils;

namespace GZCTF.Features.Imports.Application;

// Parser output is private server-side data: never serialize it to preview responses.
public sealed record PlatformImportIssue(string Severity, string Code, string SourceId, string Message);
public sealed record PlatformImportAttachment(string ArchivePath, string FileName, string Sha256, string? Flag = null, string? OriginalUrl = null,
    IReadOnlyList<string>? OriginalUrls = null, bool IsBodyResource = false, string? RemoteUrl = null);
public sealed record PlatformImportFlag(string Kind, string Value, bool IgnoreCase = false);
public sealed record PlatformImportContainer(string Image, int ExposedPort, int Cpu, int MemoryMb, int StorageMb,
    string NetworkMode, string FlagMode = "native");
public sealed record PlatformImportChallenge(string SourceId, string Title, string Body, string Category,
    ChallengeType Type, string Difficulty, int Score, bool IsEnabled, int SubmissionLimit,
    IReadOnlyList<PlatformImportFlag> Flags, IReadOnlyList<string> Hints,
    IReadOnlyList<PlatformImportAttachment> Attachments, PlatformImportContainer? Container, string MetadataJson);
public sealed record PlatformImportUser(string SourceId, string UserName, string? Email, string? PasswordHash,
    bool Verified, bool Banned, string? TeamId, DateTimeOffset? CreatedAtUtc, string MetadataJson);
public sealed record PlatformImportTeam(string SourceId, string Name, string MetadataJson);
public sealed record PlatformImportSubmission(string SourceId, string? UserId, string? TeamId, string ChallengeId,
    string Result, DateTimeOffset? SubmittedAtUtc, int? Score, string MetadataJson);
public sealed record PlatformImportAward(string SourceId, string? UserId, string? TeamId, string Name, int Value,
    DateTimeOffset? AwardedAtUtc, string MetadataJson);
public sealed record PlatformImportPackage(string Format, string Fingerprint, string Title,
    IReadOnlyList<PlatformImportChallenge> Challenges, IReadOnlyList<PlatformImportUser> Users,
    IReadOnlyList<PlatformImportTeam> Teams, IReadOnlyList<PlatformImportSubmission> Submissions,
    IReadOnlyList<PlatformImportAward> Awards, IReadOnlyDictionary<string, byte[]> Files,
    IReadOnlyList<PlatformImportIssue> Issues, string MetadataJson);

public sealed record PlatformImportOptions(string SourceInstance, string? TreeName = null, Guid? TargetSkillId = null,
    uint? TargetSkillRowVersion = null, bool IncludeAccounts = true, bool IncludeHistory = true,
    IReadOnlyDictionary<string, string>? UserNameOverrides = null, IReadOnlyList<string>? SkipUserIds = null);
public sealed record PlatformImportPreview(string Format, string Fingerprint, string Title, int ChallengeCount,
    int FileCount, int UserCount, int TeamCount, int SubmissionCount, int AwardCount,
    IReadOnlyList<PlatformImportIssue> Issues, bool CanImport, string PlanFingerprint);
