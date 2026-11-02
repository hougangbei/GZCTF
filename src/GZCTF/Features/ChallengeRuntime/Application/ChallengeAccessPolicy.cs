using GZCTF.Models.Data;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.ChallengeRuntime.Application;

public sealed class ChallengeAccessPolicy(AppDbContext db)
{
    public Task<bool> CanReadAsync(Guid challengeId, UserInfo? user, CancellationToken token) =>
        user?.Role == Role.Admin ? db.Challenges.AsNoTracking().AnyAsync(item => item.Id == challengeId, token) : IsPublicAsync(db, challengeId, token);
    public Task<bool> CanAccessAsync(Guid challengeId, UserInfo user, CancellationToken token) => CanReadAsync(challengeId, user, token);
    internal static Task<bool> IsPublicAsync(AppDbContext db, Guid id, CancellationToken token) =>
        db.SkillContents.AsNoTracking().AnyAsync(content => content.ChallengeId == id && content.Challenge!.IsEnabled &&
            content.Challenge.PublicationState == Features.ChallengeLibrary.Domain.ChallengePublicationState.Published &&
            content.Skill.DeletedAtUtc == null && content.Skill.IsEnabled &&
            content.Skill.Categories.Any(reference => reference.Category.DeletedAtUtc == null &&
                reference.Category.SkillTrees.Any(tree => tree.Revision.SkillTree.DeletedAtUtc == null &&
                    tree.Revision.SkillTree.CurrentPublishedRevisionId == tree.RevisionId)), token);
}
