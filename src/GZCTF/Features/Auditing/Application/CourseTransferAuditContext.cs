namespace GZCTF.Features.Auditing.Application;

// Explicitly typed and populated by the server. Never serialize action arguments into audit details.
public sealed record CourseTransferAuditPath(Guid TreeId, Guid CategoryId, Guid SkillId,
    string? TreeName = null, string? CategoryName = null, string? SkillName = null);
public sealed record CourseTransferAuditContent(string Kind, Guid ContentId);
public sealed record CourseTransferAuditContext(Guid OperationId, string Mode, CourseTransferAuditPath Source,
    CourseTransferAuditPath Target, CourseTransferAuditContent[] Contents)
{
    public static readonly object ItemKey = new();
}
