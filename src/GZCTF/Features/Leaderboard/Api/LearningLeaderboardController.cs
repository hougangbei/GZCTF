using GZCTF.Features.Leaderboard.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Features.Leaderboard.Api;

[AllowAnonymous]
[ApiController]
[Route("api/learning-leaderboard")]
public sealed class LearningLeaderboardController(LearningLeaderboardService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<LearningLeaderboardResponse>> Get(
        [FromQuery] string metric = "score", CancellationToken token = default)
    {
        if (metric is not ("score" or "solves"))
            return BadRequest("Metric must be 'score' or 'solves'.");
        return Ok(await service.GetAsync(metric, token));
    }
}
