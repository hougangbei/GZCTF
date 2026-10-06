namespace GZCTF.Features.AboutPage.Domain;

/// <summary>Singleton workflow state for the laboratory about page.</summary>
public sealed class AboutPageState
{
    public int Id { get; set; } = 1;
    public string DraftJson { get; set; } = "{\"schemaVersion\":1,\"title\":\"关于实验室\",\"sections\":[]}";
    public long DraftRevision { get; set; }
    public Guid? CurrentVersionId { get; set; }
    public Guid? LockOwnerId { get; set; }
    public string? LockOwnerName { get; set; }
    public DateTimeOffset? LockCreatedAtUtc { get; set; }
    public DateTimeOffset DraftUpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>An immutable published snapshot.</summary>
public sealed class AboutPageVersion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public long VersionNumber { get; set; }
    public string DocumentJson { get; set; } = string.Empty;
    public Guid PublisherId { get; set; }
    public string PublisherName { get; set; } = string.Empty;
    public DateTimeOffset PublishedAtUtc { get; set; }
    public long SourceDraftRevision { get; set; }
}
