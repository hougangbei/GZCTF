using GZCTF.Repositories.Interface;
using GZCTF.Services.Traffic;
using GZCTF.Models;
using GZCTF.Features.ChallengeRuntime.Domain;
using Microsoft.EntityFrameworkCore;

// ReSharper disable UnusedMember.Global

namespace GZCTF.Services.CronJob;

public static class RuntimeCronJobs
{
    [CronJob("*/3 * * * *")]
    public static async Task ContainerChecker(AsyncServiceScope scope, ILogger<CronJobService> logger)
    {
        var containerRepo = scope.ServiceProvider.GetRequiredService<IContainerRepository>();
        var trafficRegistry = scope.ServiceProvider.GetRequiredService<TrafficRecorderRegistry>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var container in await containerRepo.GetDyingContainers())
        {
            await trafficRegistry.ArchiveAsync(container.Id);
            if (!await containerRepo.DestroyContainer(container)) continue;
            await db.UserChallengeInstances
                .Where(item => item.ContainerId == container.Id && item.IsActive)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(item => item.Status, ChallengeInstanceStatus.Expired)
                    .SetProperty(item => item.IsActive, false)
                    .SetProperty(item => item.StoppedAtUtc, DateTimeOffset.UtcNow));
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.CronJob_RemoveExpiredContainer),
                    container.LogId],
                TaskStatus.Success, LogLevel.Debug);
        }
    }
}
