using System.Net.Mime;
using GZCTF.Features.SkillTrees.Application;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Features.SkillTrees.Api;

[ApiController]
[Route("api/skill-trees")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class SkillTreesController(SkillTreeQueryService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SkillTreeSummaryResponse>>> List(CancellationToken token) =>
        Ok(await service.ListPublishedAsync(token));

    [HttpGet("{treeId:guid}/categories/{categoryId:guid}/skills/{skillId:guid}")]
    public async Task<IActionResult> Skill(Guid treeId, Guid categoryId, Guid skillId, CancellationToken token) =>
        await service.GetSkillAsync(treeId, categoryId, skillId, token) is { } result ? Ok(result) : NotFound();

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SkillTreeDetailResponse>> Detail(Guid id, CancellationToken token) =>
        await service.GetPublishedAsync(id, token) is { } result ? Ok(result) : NotFound();
}
