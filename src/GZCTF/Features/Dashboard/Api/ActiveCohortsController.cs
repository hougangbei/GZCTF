using GZCTF.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.Dashboard.Api;

public sealed record ActiveCohortResponse(Guid Id, string Name);

[AllowAnonymous]
[ApiController]
[Route("api/cohorts/active")]
public sealed class ActiveCohortsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ActiveCohortResponse>>> List(CancellationToken token) =>
        Ok(await db.Cohorts.AsNoTracking()
            .Where(cohort => cohort.IsActive)
            .OrderBy(cohort => cohort.Name)
            .Select(cohort => new ActiveCohortResponse(cohort.Id, cohort.Name))
            .ToArrayAsync(token));
}
