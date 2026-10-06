using GZCTF.Features.Auditing.Domain;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.ChallengeRuntime.Domain;
using GZCTF.Features.Dashboard.Domain;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.Auditing.Infrastructure;

public sealed class AuditTargetResolver(AppDbContext db)
{
    public async Task<string?> GetNameAsync(string targetType, string? targetId, CancellationToken token)
    {
        if (targetType == "post")
            return targetId is null ? null : await db.Posts.AsNoTracking().Where(item => item.Id == targetId)
                .Select(item => item.Title).SingleOrDefaultAsync(token);
        if (targetType == "asset")
            return targetId is null ? null : await db.Files.AsNoTracking().Where(item => item.Hash == targetId)
                .Select(item => item.Name).SingleOrDefaultAsync(token);
        if (!Guid.TryParse(targetId, out var id)) return targetType switch
        {
            "configuration" => "Platform configuration",
            "logo" => "Platform logo",
            "qq_settings" => "QQ bot settings",
            "qq_test" => "QQ test message",
            "challenge_instance_settings" => "Challenge instance settings",
            "dashboard_projection" => "Daily solve projection",
            "batch" or "cohort_batch" => null,
            "asset" => null,
            _ => null
        };

        return targetType switch
        {
            "user" => await db.Users.AsNoTracking().Where(item => item.Id == id).Select(item => item.UserName).SingleOrDefaultAsync(token),
            "challenge" => await db.Challenges.AsNoTracking().Where(item => item.Id == id)
                .Select(item => item.Localizations.OrderBy(value => value.Locale == "zh-CN" ? 0 : 1).Select(value => value.Title).FirstOrDefault())
                .SingleOrDefaultAsync(token),
            "lesson" => await db.Lessons.AsNoTracking().Where(item => item.Id == id)
                .Select(item => item.Localizations.OrderBy(value => value.Locale == "zh-CN" ? 0 : 1).Select(value => value.Title).FirstOrDefault())
                .SingleOrDefaultAsync(token),
            "skill_tree" => await db.SkillTrees.AsNoTracking().Where(item => item.Id == id).Select(item => item.Name).SingleOrDefaultAsync(token),
            "skill_category" => await db.SkillCategories.AsNoTracking().Where(item => item.Id == id).Select(item => item.Name).SingleOrDefaultAsync(token),
            "cohort" => await db.Cohorts.AsNoTracking().Where(item => item.Id == id).Select(item => item.Name).SingleOrDefaultAsync(token),
            "cohort_member" => await db.Users.AsNoTracking().Where(item => item.Id == id).Select(item => item.UserName).SingleOrDefaultAsync(token),
            "dashboard" => await db.Dashboards.AsNoTracking().Where(item => item.Id == id).Select(item => item.Name).SingleOrDefaultAsync(token),
            "api_token" => await db.ApiTokens.AsNoTracking().Where(item => item.Id == id).Select(item => item.Name).SingleOrDefaultAsync(token),
            "dashboard_token" => "Dashboard token",
            "import_batch" => await db.MigrationBatches.AsNoTracking().Where(item => item.Id == id).Select(item => item.SourceType).SingleOrDefaultAsync(token),
            "writeup" => await db.CommunityWriteups.AsNoTracking().Where(item => item.Id == id).Select(item => item.Title).SingleOrDefaultAsync(token),
            "container" => await db.Containers.AsNoTracking().Where(item => item.Id == id).Select(item => item.Image).SingleOrDefaultAsync(token),
            "challenge_instance" => await db.UserChallengeInstances.AsNoTracking().Where(item => item.Id == id)
                .Select(item => item.Challenge.Localizations.OrderBy(value => value.Locale == "zh-CN" ? 0 : 1).Select(value => value.Title).FirstOrDefault())
                .SingleOrDefaultAsync(token),
            _ => null
        };
    }
}
