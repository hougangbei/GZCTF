using GZCTF.Features.Shared;
using GZCTF.Features.SkillTrees.Application;
using GZCTF.Middlewares;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Features.SkillTrees.Api;

[RequireAdmin, ApiController, Route("api/admin/skills")]
public sealed class AdminSkillsController(SkillService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken token) => Ok(await service.ListAsync(token));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken token) => await service.GetAsync(id, token) is { } result ? Ok(result) : NotFound();
    [HttpPost, AuditAction("skills.create")]
    public Task<IActionResult> Create(SkillCommand command, CancellationToken token) => Execute(async () => Ok(await service.CreateAsync(command, token)));
    [HttpPut("{id:guid}"), AuditAction("skills.update")]
    public Task<IActionResult> Update(Guid id, SkillCommand command, CancellationToken token) => Execute(async () => await service.UpdateAsync(id, command, token) is { } result ? Ok(result) : NotFound());
    [HttpPut("{id:guid}/contents"), AuditAction("skills.contents.update")]
    public Task<IActionResult> Contents(Guid id, SkillContentUpdateCommand command, CancellationToken token) => Execute(async () => await service.UpdateContentsAsync(id, command, token) is { } result ? Ok(result) : NotFound());
    [HttpGet("{id:guid}/delete-impact")]
    public async Task<IActionResult> Impact(Guid id, CancellationToken token) => await service.GetDeleteImpactAsync(id, token) is { } result ? Ok(result) : NotFound();
    [HttpDelete("{id:guid}"), AuditAction("skills.delete")]
    public Task<IActionResult> Delete(Guid id, DeleteSkillCommand command, CancellationToken token) => Execute(async () => await service.DeleteAsync(id, command, token) ? NoContent() : NotFound());
    [HttpPut("/api/admin/skill-categories/{id:guid}/skills"), AuditAction("skill_categories.skills.update")]
    public Task<IActionResult> CategorySkills(Guid id, CategorySkillsUpdateCommand command, CancellationToken token) => Execute(async () => await service.UpdateCategorySkillsAsync(id, command, token) is { } result ? Ok(result) : NotFound());
    private async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (SkillTreeRevisionConflictException) { return Conflict(ApiError.Conflict(SkillTreeProblemCodes.RevisionConflict, "The item was changed by another administrator.", HttpContext.TraceIdentifier)); }
        catch (SkillTreeInvalidIconException) { return BadRequest(ApiError.Validation(SkillTreeProblemCodes.InvalidIcon, "The icon is not supported.", HttpContext.TraceIdentifier, null)); }
        catch (SkillTreeConfirmationMismatchException) { return BadRequest(ApiError.Validation(SkillTreeProblemCodes.ConfirmationMismatch, "The confirmation name does not match.", HttpContext.TraceIdentifier, null)); }
        catch (SkillCategoryValidationException exception) { return BadRequest(ApiError.Validation("skill_invalid", exception.Message, HttpContext.TraceIdentifier, null)); }
    }
}
