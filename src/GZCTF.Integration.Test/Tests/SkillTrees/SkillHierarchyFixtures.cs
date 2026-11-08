using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Integration.Test.Tests.SkillTrees;

/// <summary>Translates the original three-level fixture builders into explicit four-level fixtures.
/// This is test data setup only: application reads never backfill or consult legacy membership.</summary>
internal static class SkillHierarchyFixtures
{
    internal static async Task<int> SaveSkillTreeFixtureAsync(this AppDbContext db)
    {
        var legacyEntries = db.ChangeTracker.Entries<CategoryContent>().Where(entry => entry.State == EntityState.Added).ToArray();
        foreach (var group in legacyEntries.GroupBy(entry => entry.Entity.CategoryId))
        {
            var categoryId = group.Key;
            var category = group.First().Entity.Category ?? await db.SkillCategories.FindAsync(categoryId) ?? throw new InvalidOperationException("Fixture category missing.");
            var skill = new Skill { Id = categoryId, Name = category.Name, Summary = category.Summary, IconKey = category.IconKey };
            db.Skills.Add(skill);
            db.CategorySkillRefs.Add(new CategorySkillRef { Category = category, Skill = skill, SortOrder = 0 });
            foreach (var entry in group)
            {
                var item = entry.Entity;
                db.SkillContents.Add(new SkillContent { Id = item.Id, Skill = skill, SortOrder = item.SortOrder,
                    LessonId = item.LessonId, Lesson = item.Lesson, ChallengeId = item.ChallengeId,
                    Challenge = item.Challenge, CreatedAtUtc = item.CreatedAtUtc });
                category.Contents.Remove(item);
                entry.State = EntityState.Detached;
            }
        }
        return await db.SaveChangesAsync();
    }
}
