using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.LearningPaths.Domain;
using GZCTF.Features.Shared;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Middlewares;
using GZCTF.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Challenge = GZCTF.Features.ChallengeLibrary.Domain.Challenge;

namespace GZCTF.Features.SkillTrees.Api;
public sealed record CreateCourseNodeCommand(Guid OperationId, string Kind, string Name, string? ChallengeType);
public sealed record CreatedCourseNode(Guid Id, string Name, string Summary, string IconKey, object[] Skills, object[] Contents, bool IsEnabled, bool Created);

[RequireAdmin, ApiController, Route("api/admin/skill-trees/{id:guid}/workspace/nodes")]
public sealed class CourseWorkspaceNodesController(AppDbContext db) : ControllerBase
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    [HttpPost, AuditAction("course_workspace.node.create")]
    public async Task<IActionResult> Create(Guid id, CreateCourseNodeCommand command, CancellationToken token)
    {
        if (command.OperationId == Guid.Empty || command.Kind is not ("category" or "skill" or "lesson" or "challenge") ||
            string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 128) return BadRequest();
        ChallengeType type = ChallengeType.StaticAttachment;
        if (command.Kind == "challenge" && command.ChallengeType is { Length: > 0 } &&
            (!Enum.TryParse(command.ChallengeType, true, out type) || !Enum.IsDefined(type))) return BadRequest();
        var name = command.Name.Trim();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { command.Kind, Name = name, Type = command.Kind == "challenge" ? type.ToString() : null }))));
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"SkillTreeId\" FROM \"CourseAuthoringWorkspaces\" WHERE \"SkillTreeId\" = {id} FOR UPDATE", token);
        if (!await db.CourseAuthoringWorkspaces.AnyAsync(w => w.SkillTreeId == id && w.SkillTree.DeletedAtUtc == null, token)) return NotFound();
        var previous = await db.CourseNodeOperations.SingleOrDefaultAsync(o => o.SkillTreeId == id && o.OperationId == command.OperationId, token);
        if (previous is not null)
            return previous.RequestHash == hash ? Ok(JsonSerializer.Deserialize<CreatedCourseNode>(previous.ResponseJson, Options)) :
                Conflict(ApiError.Conflict("course_node_operation_conflict", "This operation ID was used for a different request.", HttpContext.TraceIdentifier));
        Guid createdId;
        switch (command.Kind)
        {
            case "category":
                var category = new SkillCategory { Name = name }; db.SkillCategories.Add(category); createdId = category.Id; break;
            case "skill":
                var skill = new Skill { Name = name }; db.Skills.Add(skill); createdId = skill.Id; break;
            case "lesson":
                var lesson = new Lesson { Localizations = [new LessonLocalization { Locale = "zh", Title = name }] };
                db.Lessons.Add(lesson); createdId = lesson.Id; break;
            default:
                var challenge = new Challenge { Type = type, Localizations = [new ChallengeLocalization { Locale = "zh", Title = name }] };
                challenge.SourceId = challenge.Id.ToString("N"); db.Challenges.Add(challenge); createdId = challenge.Id; break;
        }
        var response = new CreatedCourseNode(createdId, name, "", "flag", [], [], true, true);
        db.CourseNodeOperations.Add(new CourseNodeOperation { SkillTreeId = id, OperationId = command.OperationId, RequestHash = hash,
            ResponseJson = JsonSerializer.Serialize(response, Options) });
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return Ok(response);
    }
}
