namespace GZCTF.Features.SkillTrees.Domain;

/// <summary>A private, independently versioned course authoring document.</summary>
public sealed class CourseAuthoringWorkspace
{
    public Guid SkillTreeId { get; set; }
    public SkillTree SkillTree { get; set; } = null!;
    public Guid Version { get; set; } = Guid.NewGuid();
    public string DocumentJson { get; set; } = string.Empty;
    public string? CoverStorageKey { get; set; }
}

/// <summary>Immutable course version; its ID is the corresponding published revision ID.</summary>
public sealed class CoursePublishedVersion
{
    public Guid Id { get; set; }
    public Guid SkillTreeId { get; set; }
    public SkillTree SkillTree { get; set; } = null!;
    public string PublicDocumentJson { get; set; } = string.Empty;
    public string? CoverStorageKey { get; set; }
    public DateTimeOffset PublishedAtUtc { get; set; }
    public List<CoursePublishedContent> Contents { get; set; } = [];
}

/// <summary>Private frozen content payload; never return this entity directly to public clients.</summary>
public sealed class CoursePublishedContent
{
    public Guid Id { get; set; }
    public Guid VersionId { get; set; }
    public CoursePublishedVersion Version { get; set; } = null!;
    public Guid ContentId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
}

public sealed class CourseWorkspaceContent
{
    public Guid SkillTreeId { get; set; }
    public CourseAuthoringWorkspace Workspace { get; set; } = null!;
    public Guid ContentId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string PayloadJson { get; set; } = string.Empty;
}

public sealed class CourseNodeOperation
{
    public Guid SkillTreeId { get; set; }
    public CourseAuthoringWorkspace Workspace { get; set; } = null!;
    public Guid OperationId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
}

public sealed class CourseStoredAsset
{
    public string StorageKey { get; set; } = string.Empty;
    public string ReferenceToken { get; set; } = string.Empty;
    public DateTimeOffset LastTouchedUtc { get; set; }
}
