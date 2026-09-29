using CanonicalChallenge = GZCTF.Features.ChallengeLibrary.Domain.Challenge;

namespace GZCTF.Features.ChallengeRuntime.Domain;

public enum CommunityWriteupStatus : byte
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public sealed class CommunityWriteup
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ChallengeId { get; set; }
    public CanonicalChallenge Challenge { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public CommunityWriteupStatus Status { get; set; } = CommunityWriteupStatus.Pending;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
}
