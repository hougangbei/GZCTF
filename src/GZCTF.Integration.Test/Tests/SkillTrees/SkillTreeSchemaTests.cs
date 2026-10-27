using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.LearningPaths.Domain;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.SkillTrees;

[Collection(nameof(IntegrationTestCollection))]
public class SkillTreeSchemaTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Tree_revision_has_unique_category_and_sort_order()
    {
        await AssertConstraintViolationAsync(async db =>
        {
            var category = new SkillCategory { Name = "Web", IconKey = "web" };
            var revision = new SkillTreeRevision
            {
                SkillTree = new SkillTree { Name = "Beginner", IconKey = "flag" },
                Status = SkillTreeRevisionStatus.Draft
            };
            revision.Categories.Add(new SkillTreeCategoryRef { Category = category, SortOrder = 0 });
            revision.Categories.Add(new SkillTreeCategoryRef { Category = category, SortOrder = 1 });
            db.SkillTreeRevisions.Add(revision);
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Category_content_references_exactly_one_content_type()
    {
        await AssertConstraintViolationAsync(async db =>
        {
            db.CategoryContents.Add(new CategoryContent
            {
                Category = new SkillCategory { Name = "Web", IconKey = "web" },
                Lesson = new Lesson(),
                Challenge = new Challenge { Type = ChallengeType.StaticAttachment },
                SortOrder = 0
            });
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task User_has_at_most_one_current_skill_tree()
    {
        var user = await TestDataSeeder.CreateUserAsync(
            factory.Services, TestDataSeeder.RandomName(), "SkillTree!123");
        await AssertConstraintViolationAsync(async db =>
        {
            db.SkillTreeEnrollments.AddRange(
                new SkillTreeEnrollment
                {
                    UserId = user.Id,
                    SkillTree = new SkillTree { Name = "Web", IconKey = "web" },
                    IsCurrent = true
                },
                new SkillTreeEnrollment
                {
                    UserId = user.Id,
                    SkillTree = new SkillTree { Name = "AI", IconKey = "ai" },
                    IsCurrent = true
                });
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Category_cannot_contain_the_same_challenge_twice()
    {
        await AssertConstraintViolationAsync(async db =>
        {
            var category = new SkillCategory { Name = "Web", IconKey = "web" };
            var challenge = new Challenge { Type = ChallengeType.StaticAttachment };
            category.Contents.Add(new CategoryContent { Challenge = challenge, SortOrder = 0 });
            category.Contents.Add(new CategoryContent { Challenge = challenge, SortOrder = 1 });
            db.SkillCategories.Add(category);
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Tree_has_at_most_one_draft_revision()
    {
        await AssertConstraintViolationAsync(async db =>
        {
            var tree = new SkillTree { Name = "Web", IconKey = "web" };
            tree.Revisions.Add(new SkillTreeRevision { Status = SkillTreeRevisionStatus.Draft });
            tree.Revisions.Add(new SkillTreeRevision { Status = SkillTreeRevisionStatus.Draft });
            db.SkillTrees.Add(tree);
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Old_slug_redirect_is_unique()
    {
        await AssertConstraintViolationAsync(async db =>
        {
            var tree = new SkillTree { Name = "Web", IconKey = "web" };
            db.LearningPathRedirects.AddRange(
                new LearningPathRedirect { LearningPathId = Guid.CreateVersion7(), OldSlug = "old-web", SkillTree = tree },
                new LearningPathRedirect { LearningPathId = Guid.CreateVersion7(), OldSlug = "old-web", SkillTree = tree });
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Unknown_icon_key_is_rejected_by_database()
    {
        await AssertConstraintViolationAsync(async db =>
        {
            db.SkillTrees.Add(new SkillTree { Name = "Web", IconKey = "arbitrary" });
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Migrated_database_accepts_every_preset_for_both_trees_and_categories()
    {
        using var scope = factory.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var key in SkillTreeIconCatalog.Values.Keys)
        {
            db.SkillTrees.Add(new SkillTree { Name = key, IconKey = key });
            db.SkillCategories.Add(new SkillCategory { Name = key, IconKey = key });
        }
        await db.SaveChangesAsync();
        Assert.Equal(40, SkillTreeIconCatalog.Values.Count);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Icon_upgrade_and_rollback_preserve_existing_records()
    {
        using var scope = factory.Services.CreateScope();
        var existing = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var databaseName = "icon_upgrade_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(existing.Database.GetConnectionString());
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(existing.Database.GetConnectionString())
            {
                Database = databaseName,
                Pooling = false
            };
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connection.ConnectionString).Options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261006200000_AddChallengeScore");
            var oldTree = new SkillTree { Name = "Existing tree", IconKey = "web" };
            var oldCategory = new SkillCategory { Name = "Existing category", IconKey = "crypto" };
            db.AddRange(oldTree, oldCategory);
            await db.SaveChangesAsync();

            await migrator.MigrateAsync();
            var newTree = new SkillTree { Name = "New tree", IconKey = "terminal" };
            var newCategory = new SkillCategory { Name = "New category", IconKey = "network" };
            db.AddRange(newTree, newCategory);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            Assert.Equal("web", (await db.SkillTrees.FindAsync(oldTree.Id))!.IconKey);
            Assert.Equal("crypto", (await db.SkillCategories.FindAsync(oldCategory.Id))!.IconKey);

            await migrator.MigrateAsync("20261006200000_AddChallengeScore");
            db.ChangeTracker.Clear();
            Assert.Equal("flag", (await db.SkillTrees.FindAsync(newTree.Id))!.IconKey);
            Assert.Equal("flag", (await db.SkillCategories.FindAsync(newCategory.Id))!.IconKey);
            Assert.Equal("web", (await db.SkillTrees.FindAsync(oldTree.Id))!.IconKey);
            await migrator.MigrateAsync();
            Assert.Equal(2, await db.SkillTrees.CountAsync());
            Assert.Equal(2, await db.SkillCategories.CountAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private async Task AssertConstraintViolationAsync(Func<AppDbContext, Task> arrangeAndSave)
    {
        using var scope = factory.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => arrangeAndSave(db));
        await transaction.RollbackAsync();
    }
}
