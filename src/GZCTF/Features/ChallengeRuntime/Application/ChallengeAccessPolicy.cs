using GZCTF.Models.Data;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.ChallengeRuntime.Application;

public sealed class ChallengeAccessPolicy(AppDbContext db)
{
    public Task<bool> CanReadAsync(Guid challengeId, UserInfo? user, CancellationToken token, Guid? courseId = null) =>
        user?.Role == Role.Admin && courseId is null ? db.Challenges.AsNoTracking().AnyAsync(item => item.Id == challengeId, token) : IsPublicAsync(db, challengeId, token, courseId);
    public Task<bool> CanAccessAsync(Guid challengeId, UserInfo user, CancellationToken token, Guid? courseId = null) => CanReadAsync(challengeId, user, token, courseId);
    internal static async Task<bool> IsPublicAsync(AppDbContext db, Guid id, CancellationToken token, Guid? courseId = null) =>
        await PublishedCourseContentReader.GetChallengeAsync(db, id, token, courseId: courseId) is not null;
}
