using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Integration.Test.Tests.SkillTrees;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Integration.Test.Base;

/// <summary>Runtime API fixtures need an explicit published hierarchy now that publication alone does not grant access.</summary>
internal static class PublishedSkillFixture
{
    internal static async Task<int> SaveRuntimeFixtureAsync(this AppDbContext db)
    {
        var challenges = db.ChangeTracker.Entries<Challenge>().Where(entry => entry.State == EntityState.Added &&
            entry.Entity.PublicationState == ChallengePublicationState.Published).Select(entry => entry.Entity).ToArray();
        var trees = new List<SkillTree>();
        foreach (var challenge in challenges)
        {
            var alreadyAssigned = db.ChangeTracker.Entries<SkillContent>().Any(entry => entry.Entity.ChallengeId == challenge.Id) ||
                db.ChangeTracker.Entries<CategoryContent>().Any(entry => entry.Entity.ChallengeId == challenge.Id);
            if (alreadyAssigned) continue;
            var skill = new Skill { Name = "Runtime fixture", Contents = [new SkillContent { Challenge = challenge, SortOrder = 0 }] };
            var category = new SkillCategory { Name = "Runtime fixture", Skills = [new CategorySkillRef { Skill = skill, SortOrder = 0 }] };
            var revision = new SkillTreeRevision { Status = SkillTreeRevisionStatus.Published, Categories = [new SkillTreeCategoryRef { Category = category, SortOrder = 0 }] };
            var tree = new SkillTree { Name = "Runtime fixture", Revisions = [revision] };
            db.Add(tree); trees.Add(tree);
        }
        var count = await db.SaveSkillTreeFixtureAsync();
        foreach (var tree in trees) tree.CurrentPublishedRevisionId = tree.Revisions.Single().Id;
        if (trees.Count > 0) count += await db.SaveChangesAsync();
        return count;
    }
}
