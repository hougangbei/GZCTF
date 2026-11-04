using System.Security.Claims;
using GZCTF.Features.Leaderboard.Application;
using GZCTF.Middlewares;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Features.Leaderboard.Api;

[RequireAdmin]
[ApiController]
[Route("api/admin/learning-rankings")]
public sealed class AdminLearningRankingsController(AdminRankingService service, RankingScoreService scores) : ControllerBase
{
    private Guid ActorId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private string ActorName => User.Identity?.Name ?? "管理员";
    [HttpGet]
    public Task<ActionResult> List([FromQuery] string? search = null, [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] bool? hidden = null, [FromQuery] bool? banned = null, [FromQuery] string? visibility = null, CancellationToken token = default)
        => Run(async () => await service.ListAsync(search, skip, take, visibility == "hidden" ? true : hidden, visibility == "banned" ? true : banned, token));
    [HttpGet("{userId:guid}")]
    public Task<ActionResult> Detail(Guid userId, CancellationToken token) => Run(async () => await service.DetailAsync(userId, token));
    [HttpPost("{userId:guid}/adjustments"), AuditAction("learning_rankings.adjust")]
    public Task<ActionResult> Adjust(Guid userId, RankingAdjustmentCommand command, CancellationToken token) => Run(async () => await service.AdjustAsync(userId, command, ActorId, ActorName, token));
    [HttpPost("adjustments/{id:guid}/reverse"), AuditAction("learning_rankings.reverse")]
    public Task<ActionResult> Reverse(Guid id, RankingReasonCommand command, CancellationToken token) => Run(async () => await service.ReverseAsync(id, command, ActorId, ActorName, token));
    [HttpPut("{userId:guid}/visibility"), AuditAction("learning_rankings.visibility")]
    public Task<ActionResult> Visibility(Guid userId, RankingVisibilityCommand command, CancellationToken token) => Run(async () => await service.VisibilityAsync(userId, command, ActorId, ActorName, token));
    [HttpPost("{userId:guid}/challenges/{challengeId:guid}/solve"), AuditAction("learning_rankings.mark_solve")]
    public Task<ActionResult> Mark(Guid userId, Guid challengeId, RankingReasonCommand command, CancellationToken token) => Run(async () => await service.SolveAsync(userId, challengeId, true, command, ActorId, ActorName, token));
    [HttpDelete("{userId:guid}/challenges/{challengeId:guid}/solve"), AuditAction("learning_rankings.revoke_solve")]
    public Task<ActionResult> Revoke(Guid userId, Guid challengeId, [FromBody] RankingReasonCommand command, CancellationToken token) => Run(async () => await service.SolveAsync(userId, challengeId, false, command, ActorId, ActorName, token));
    [HttpGet("operations")]
    public Task<ActionResult> Operations([FromQuery] Guid? userId = null, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken token = default) => Run(async () => await service.OperationsAsync(userId, skip, take, token));
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search = null, [FromQuery] bool? hidden = null, [FromQuery] bool? banned = null, CancellationToken token = default) => File(await service.ExportAsync(search, hidden, banned, token), "text/csv; charset=utf-8", "learning-rankings.csv");
    [HttpGet("levels")]
    public Task<ActionResult> Levels(CancellationToken token) => Run(async () => await scores.LevelsAsync(token));
    [HttpPost("levels/preview")]
    [AuditAction("learning_rankings.levels.preview")]
    public Task<ActionResult> Preview(RankingLevelsPreviewCommand command, CancellationToken token) => Run(async () => new { affectedUserCount = await service.PreviewLevelsAsync(command, token) });
    [HttpPut("levels"), AuditAction("learning_rankings.levels")]
    public Task<ActionResult> SetLevels(RankingLevelsCommand command, CancellationToken token) => Run(async () => await service.SetLevelsAsync(command, ActorId, ActorName, token));
    private async Task<ActionResult> Run(Func<Task<object>> action)
    {
        try
        { return Ok(await action()); }
        catch (RankingConflictException e) { return Conflict(new { code = e.Message }); }
        catch (RankingValidationException e) { return BadRequest(new { code = e.Message }); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
