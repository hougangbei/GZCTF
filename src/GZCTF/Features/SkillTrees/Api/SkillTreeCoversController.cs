using GZCTF.Features.Shared;
using GZCTF.Features.SkillTrees.Application;
using GZCTF.Middlewares;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Features.SkillTrees.Api;

[RequireAdmin, ApiController, Route("api/admin/skill-trees/{id:guid}/cover")]
public sealed class SkillTreeCoversController(SkillTreeCoverService service) : ControllerBase
{
    [HttpPost, AuditAction("skill_trees.cover.update")]
    [RequestSizeLimit(SkillTreeCoverService.MaxBytes + 65536)]
    public Task<IActionResult> Upload(Guid id, [FromForm] IFormFile file, [FromForm] uint rowVersion, CancellationToken token) =>
        Execute(() => service.SetAsync(id, rowVersion, file, token));
    [HttpDelete, AuditAction("skill_trees.cover.delete")]
    public Task<IActionResult> Remove(Guid id, DeleteSkillTreeCoverCommand command, CancellationToken token) =>
        Execute(() => service.SetAsync(id, command.RowVersion, null, token));
    private async Task<IActionResult> Execute(Func<Task<SkillTreeCoverResponse?>> action)
    {
        try { return await action() is { } result ? Ok(result) : NotFound(); }
        catch (SkillTreeRevisionConflictException) { return Conflict(ApiError.Conflict(SkillTreeProblemCodes.RevisionConflict, "The tree changed. Reload it before saving.", HttpContext.TraceIdentifier)); }
        catch (SkillCategoryValidationException exception) { return BadRequest(ApiError.Validation("skill_tree_invalid_cover", exception.Message, HttpContext.TraceIdentifier, null)); }
    }
}
