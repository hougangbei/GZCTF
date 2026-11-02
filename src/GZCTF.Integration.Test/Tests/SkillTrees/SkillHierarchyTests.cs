using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Xunit;
using GZCTF.Models;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Features.SkillTrees.Application;
using GZCTF.Features.LearningPaths.Domain;
using GZCTF.Features.LearningProgress.Domain;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.ChallengeRuntime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Microsoft.AspNetCore.Http;
using SkiaSharp;
using GZCTF.Storage.Interface;

namespace GZCTF.Integration.Test.Tests.SkillTrees;

[Collection(nameof(IntegrationTestCollection))]
public class SkillHierarchyTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Skill_admin_requires_auth_and_validates_metadata_and_versions()
    {
        using var anonymous = factory.CreateClient();
        var denied = await anonymous.GetAsync("/api/admin/skills");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var admin = factory.CreateClient();
        var name = TestDataSeeder.RandomName();
        await TestDataSeeder.CreateUserAsync(factory.Services, name, "SkillTree!123", role: Role.Admin);
        (await admin.PostAsJsonAsync("/api/Account/Login", new LoginModel { UserName = name, Password = "SkillTree!123" })).EnsureSuccessStatusCode();
        var invalid = await admin.PostAsJsonAsync("/api/admin/skills", new { name = "", summary = "", iconKey = "bogus", isEnabled = true });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var created = await admin.PostAsJsonAsync("/api/admin/skills", new { name = "Reusable skill", summary = "Basics", iconKey = "web", isEnabled = true });
        created.EnsureSuccessStatusCode();
        var skill = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = skill.GetProperty("skillId").GetGuid();
        Assert.True(skill.GetProperty("rowVersion").GetUInt32() > 0);
        var stale = await admin.PutAsJsonAsync($"/api/admin/skills/{id}", new { name = "Changed", summary = "", iconKey = "web", isEnabled = true, rowVersion = 0 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }
    [Fact]
    public async Task Shared_skill_reuses_content_progress_and_unlink_revokes_only_last_path()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "SkillTree!123");
        var other = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "SkillTree!123");
        var challenge = new Challenge { PublicationState = ChallengePublicationState.Published, IsEnabled = true, Type = ChallengeType.StaticAttachment };
        challenge.Localizations.Add(new ChallengeLocalization { Locale = "en", Title = "Shared challenge", Body = "Description" });
        var lesson = new Lesson { PublicationState = LessonPublicationState.Published };
        lesson.Localizations.Add(new LessonLocalization { Locale = "en", Title = "Shared lesson", Body = "Content" });
        var disabled = new Challenge { PublicationState = ChallengePublicationState.Published, IsEnabled = false, Type = ChallengeType.StaticAttachment };
        disabled.Localizations.Add(new ChallengeLocalization { Locale = "en", Title = "Disabled", Body = "Description" });
        var draft = new Lesson { PublicationState = LessonPublicationState.Draft };
        draft.Localizations.Add(new LessonLocalization { Locale = "en", Title = "Secret draft", Body = "Secret" });
        var skill = new Skill { Name = "Shared skill", Contents = [new SkillContent { Challenge = challenge, SortOrder = 0 }, new SkillContent { Lesson = lesson, SortOrder = 1 }, new SkillContent { Lesson = draft, SortOrder = 2 }, new SkillContent { Challenge = disabled, SortOrder = 3 }] };
        var first = new SkillCategory { Name = "First", Skills = [new CategorySkillRef { Skill = skill, SortOrder = 0 }] };
        var second = new SkillCategory { Name = "Second", Skills = [new CategorySkillRef { Skill = skill, SortOrder = 0 }] };
        var revision = new SkillTreeRevision { Status = SkillTreeRevisionStatus.Published, Categories = [new SkillTreeCategoryRef { Category = first, SortOrder = 0 }, new SkillTreeCategoryRef { Category = second, SortOrder = 1 }] };
        var tree = new SkillTree { Name = "Reusable tree", Revisions = [revision] };
        db.SkillTrees.Add(tree);
        await db.SaveChangesAsync();
        tree.CurrentPublishedRevisionId = revision.Id;
        db.LessonProgress.Add(new LessonProgress { UserId = user.Id, LessonId = lesson.Id });
        db.ChallengeProgress.Add(new ChallengeProgress { UserId = other.Id, ChallengeId = challenge.Id });
        await db.SaveChangesAsync();
        var query = scope.ServiceProvider.GetRequiredService<SkillTreeQueryService>();
        var detail = (await query.GetPublishedAsync(tree.Id, default))!;
        Assert.Equal(1, detail.SkillCount);
        Assert.Equal(2, detail.Categories.Count);
        Assert.All(detail.Categories, category => { Assert.Single(category.Skills!); Assert.Equal(2, category.Contents.Count); });
        Assert.Null(await query.GetSkillAsync(tree.Id, Guid.CreateVersion7(), skill.Id, default));
        using var publicClient = factory.CreateClient();
        var skillJson = await publicClient.GetFromJsonAsync<JsonElement>($"/api/skill-trees/{tree.Id}/categories/{first.Id}/skills/{skill.Id}");
        Assert.Equal(skill.Id, skillJson.GetProperty("skillId").GetGuid());
        Assert.Equal(1, skillJson.GetProperty("challengeCount").GetInt32());
        Assert.Equal(1, skillJson.GetProperty("lessonCount").GetInt32());
        Assert.Equal(2, skillJson.GetProperty("contents").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync($"/api/skill-trees/{tree.Id}/categories/{Guid.CreateVersion7()}/skills/{skill.Id}")).StatusCode);
        var enrollment = scope.ServiceProvider.GetRequiredService<SkillTreeEnrollmentService>();
        var progress = (await enrollment.GetTreeProgressAsync(user.Id, tree.Id, default))!;
        Assert.Equal(2, progress.TotalCount); Assert.Equal(1, progress.CompletedCount);
        Assert.Single(progress.CompletedLessonIds); Assert.Empty(progress.SolvedChallengeIds); Assert.Single(progress.Skills);
        Assert.All(progress.Categories, category => Assert.Equal(1, category.CompletedCount));
        using var learnerClient = factory.CreateClient();
        (await learnerClient.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = user.UserName, Password = "SkillTree!123" })).EnsureSuccessStatusCode();
        var response = await learnerClient.GetAsync($"/api/my-learning/skill-trees/{tree.Id}");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var progressJson = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, progressJson.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, progressJson.GetProperty("completedCount").GetInt32());
        Assert.Empty(progressJson.GetProperty("solvedChallengeIds").EnumerateArray());
        var admin = (await scope.ServiceProvider.GetRequiredService<SkillService>().GetAsync(skill.Id, default))!;
        Assert.Equal(4, admin.Contents.Count);
        Assert.Contains(admin.Contents, content => content.ContentId == disabled.Id && content.State == "Disabled");
        var access = scope.ServiceProvider.GetRequiredService<ChallengeAccessPolicy>();
        var learner = await db.Users.SingleAsync(item => item.Id == user.Id);
        Assert.True(await access.CanReadAsync(challenge.Id, learner, default));
        Assert.Equal(LessonAccess.Allowed, await enrollment.GetLessonAccessAsync(lesson.Id, default));
        var skills = scope.ServiceProvider.GetRequiredService<SkillService>();
        await skills.UpdateCategorySkillsAsync(first.Id, new(first.RowVersion, []), default);
        Assert.True(await access.CanReadAsync(challenge.Id, learner, default));
        await skills.UpdateCategorySkillsAsync(second.Id, new(second.RowVersion, []), default);
        Assert.False(await access.CanReadAsync(challenge.Id, learner, default));
        Assert.Equal(LessonAccess.NotFound, await enrollment.GetLessonAccessAsync(lesson.Id, default));
        Assert.Equal(4, await db.SkillContents.CountAsync(content => content.SkillId == skill.Id));
        Assert.Equal(1, await db.LessonProgress.CountAsync(item => item.UserId == user.Id && item.LessonId == lesson.Id));
        Assert.False(await db.CategoryContents.AnyAsync(content => content.CategoryId == first.Id || content.CategoryId == second.Id));
    }

    [Fact]
    public async Task Ambiguous_legacy_category_write_requires_skill_and_never_touches_legacy_table()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = new SkillCategory { Name = "Ambiguous", Skills = [new CategorySkillRef { Skill = new Skill { Name = "One" }, SortOrder = 0 }, new CategorySkillRef { Skill = new Skill { Name = "Two" }, SortOrder = 1 }] };
        db.Add(category); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<SkillSelectionRequiredException>(() => scope.ServiceProvider.GetRequiredService<SkillCategoryService>()
            .UpdateContentsAsync(category.Id, new(category.RowVersion, []), default));
        Assert.False(await db.CategoryContents.AnyAsync(content => content.CategoryId == category.Id));
        var service = scope.ServiceProvider.GetRequiredService<SkillService>();
        await Assert.ThrowsAsync<SkillCategoryValidationException>(() => service.UpdateCategorySkillsAsync(category.Id,
            new(category.RowVersion, [new(category.Skills[0].SkillId, 0), new(category.Skills[1].SkillId, 0)]), default));
    }

    [Fact]
    public async Task Upgrade_creates_one_default_skill_per_nonempty_category_and_preserves_content_ids_and_order()
    {
        using var scope = factory.Services.CreateScope();
        var existing = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var databaseName = "skill_upgrade_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(existing.Database.GetConnectionString());
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(existing.Database.GetConnectionString()) { Database = databaseName, Pooling = false };
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261027030000_ExpandSkillTreeIcons");
            var category = new SkillCategory { Name = "Existing", IconKey = "web", Contents = [new CategoryContent { Lesson = new Lesson(), SortOrder = 4 }, new CategoryContent { Challenge = new Challenge { Type = ChallengeType.StaticAttachment }, SortOrder = 9 }] };
            var empty = new SkillCategory { Name = "Empty" };
            db.AddRange(category, empty); await db.SaveChangesAsync();
            var originalIds = category.Contents.OrderBy(content => content.SortOrder).Select(content => content.Id).ToArray();
            var learner = new GZCTF.Models.Data.UserInfo { UserName = "old-learner" };
            db.Users.Add(learner);
            var lessonProgress = new LessonProgress { User = learner, LessonId = category.Contents[0].LessonId!.Value };
            db.LessonProgress.Add(lessonProgress); await db.SaveChangesAsync();
            await migrator.MigrateAsync(); db.ChangeTracker.Clear();
            Assert.Equal(1, await db.Skills.CountAsync());
            Assert.Equal(category.Id, await db.Skills.Select(skill => skill.Id).SingleAsync());
            Assert.Equal("Existing", await db.Skills.Select(skill => skill.Name).SingleAsync());
            Assert.Equal(category.Id, await db.CategorySkillRefs.Select(reference => reference.CategoryId).SingleAsync());
            Assert.Equal(originalIds, await db.SkillContents.OrderBy(content => content.SortOrder).Select(content => content.Id).ToArrayAsync());
            Assert.Equal(new[] { 4, 9 }, await db.SkillContents.OrderBy(content => content.SortOrder).Select(content => content.SortOrder).ToArrayAsync());
            Assert.Equal(2, await db.CategoryContents.CountAsync());
            Assert.Equal(lessonProgress.Id, await db.LessonProgress.Select(progress => progress.Id).SingleAsync());
            Assert.Equal(lessonProgress.LessonId, await db.LessonProgress.Select(progress => progress.LessonId).SingleAsync());
            await migrator.MigrateAsync(); Assert.Equal(1, await db.Skills.CountAsync());
            var rejected = await Assert.ThrowsAsync<NotSupportedException>(() => migrator.MigrateAsync("20261027030000_ExpandSkillTreeIcons"));
            Assert.Contains("backup", rejected.Message);
            Assert.Equal(originalIds, await db.SkillContents.OrderBy(content => content.SortOrder).Select(content => content.Id).ToArrayAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Cover_validates_decoded_bytes_and_detach_preserves_shared_image()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var first = new SkillTree { Name = "First cover" }; var second = new SkillTree { Name = "Second cover" };
        db.AddRange(first, second); await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<SkillTreeCoverService>();
        using var forged = new MemoryStream("<svg><script>bad</script></svg>"u8.ToArray());
        await Assert.ThrowsAsync<SkillCategoryValidationException>(() => service.SetAsync(first.Id, first.RowVersion,
            new FormFile(forged, 0, forged.Length, "file", "image.png") { Headers = new HeaderDictionary(), ContentType = "image/png" }, default));
        using var oversized = new MemoryStream(new byte[SkillTreeCoverService.MaxBytes + 1]);
        await Assert.ThrowsAsync<SkillCategoryValidationException>(() => service.SetAsync(first.Id, first.RowVersion,
            new FormFile(oversized, 0, oversized.Length, "file", "huge.png"), default));
        using var bitmap = new SKBitmap(1600, 1200); bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var input = new MemoryStream(encoded.ToArray());
        var uploaded = (await service.SetAsync(first.Id, first.RowVersion, new FormFile(input, 0, input.Length, "file", "cover.png"), default))!;
        Assert.Matches("^/Assets/[0-9a-f]{64}/skill-tree-cover\\.webp$", uploaded.CoverUrl!);
        var hash = first.CoverHash!;
        input.Position = 0;
        var shared = (await service.SetAsync(second.Id, second.RowVersion, new FormFile(input, 0, input.Length, "file", "cover.png"), default))!;
        Assert.Equal(uploaded.CoverUrl, shared.CoverUrl);
        await Assert.ThrowsAsync<SkillTreeRevisionConflictException>(() => service.SetAsync(first.Id, 0, null, default));
        var removed = (await service.SetAsync(first.Id, uploaded.RowVersion, null, default))!;
        Assert.Null(removed.CoverUrl); Assert.Equal(hash, second.CoverHash);
        var storage = scope.ServiceProvider.GetRequiredService<IBlobStorage>();
        var path = StoragePath.Combine("uploads", hash[..2], hash[2..4], hash);
        Assert.True(await storage.ExistsAsync(path));
        await using var stored = await storage.OpenReadAsync(path);
        using var thumbnail = SKBitmap.Decode(stored);
        Assert.Equal(1280, thumbnail.Width); Assert.Equal(720, thumbnail.Height);
        Assert.True((await storage.GetBlobAsync(path)).Size <= SkillTreeCoverService.MaxBytes);
    }

    [Fact]
    public async Task Skill_soft_delete_keeps_shared_content_and_rejects_stale_versions()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lesson = new Lesson(); var skill = new Skill { Name = "Delete skill", Contents = [new SkillContent { Lesson = lesson, SortOrder = 0 }] };
        db.Add(skill); await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<SkillService>();
        var impact = (await service.GetDeleteImpactAsync(skill.Id, default))!;
        Assert.Equal(1, impact.LessonCount); Assert.True(impact.RequiresTypedConfirmation);
        await Assert.ThrowsAsync<SkillTreeConfirmationMismatchException>(() => service.DeleteAsync(skill.Id, new("wrong", impact.RowVersion), default));
        var updated = (await service.UpdateAsync(skill.Id, new("Delete skill", "Updated", "web", true, impact.RowVersion), default))!;
        await Assert.ThrowsAsync<SkillTreeRevisionConflictException>(() => service.UpdateContentsAsync(skill.Id, new(impact.RowVersion, []), default));
        Assert.True(await service.DeleteAsync(skill.Id, new("Delete skill", updated.RowVersion), default));
        Assert.Null(await service.GetAsync(skill.Id, default));
        Assert.True(await db.Lessons.AnyAsync(item => item.Id == lesson.Id));
        Assert.True(await db.SkillContents.AnyAsync(item => item.SkillId == skill.Id));
    }

    [Fact]
    public async Task Republishing_retains_existing_content_identity_and_order()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lesson = new Lesson { PublicationState = LessonPublicationState.Published,
            Localizations = [new LessonLocalization { Locale = "en", Title = "First", Body = "Content" }] };
        var other = new Lesson { PublicationState = LessonPublicationState.Published };
        var retained = new SkillContent { Lesson = lesson, SortOrder = 0 };
        var following = new SkillContent { Lesson = other, SortOrder = 1 };
        var skill = new Skill { Name = "Stable", Contents = [retained, following] };
        var category = new SkillCategory { Name = "Stable", Skills = [new CategorySkillRef { Skill = skill, SortOrder = 0 }] };
        var revision = new SkillTreeRevision { Status = SkillTreeRevisionStatus.Published, Categories = [new SkillTreeCategoryRef { Category = category, SortOrder = 0 }] };
        var tree = new SkillTree { Name = "Stable", Revisions = [revision] };
        db.Add(tree); await db.SaveChangesAsync(); tree.CurrentPublishedRevisionId = revision.Id; await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<ContentPublicationService>().PublishLessonAsync(lesson.Id,
            new PublishContentCommand(lesson.RowVersion, SkillIds: [skill.Id]), default);
        db.ChangeTracker.Clear();
        var contents = await db.SkillContents.Where(content => content.SkillId == skill.Id).OrderBy(content => content.SortOrder).ToListAsync();
        Assert.Equal(new[] { retained.Id, following.Id }, contents.Select(content => content.Id));
        Assert.Equal(new[] { 0, 1 }, contents.Select(content => content.SortOrder));
    }

    [Theory]
    [InlineData("duplicate-content")]
    [InlineData("duplicate-order")]
    [InlineData("content-xor")]
    [InlineData("negative-order")]
    [InlineData("duplicate-skill")]
    [InlineData("invalid-icon")]
    public async Task Database_enforces_skill_hierarchy_invariants(string violation)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var lesson = new Lesson(); var skill = new Skill { Name = "Constraint skill" };
        switch (violation)
        {
            case "duplicate-content": skill.Contents = [new SkillContent { Lesson = lesson, SortOrder = 0 }, new SkillContent { Lesson = lesson, SortOrder = 1 }]; break;
            case "duplicate-order": skill.Contents = [new SkillContent { Lesson = lesson, SortOrder = 0 }, new SkillContent { Lesson = new Lesson(), SortOrder = 0 }]; break;
            case "content-xor": skill.Contents = [new SkillContent { SortOrder = 0 }]; break;
            case "negative-order": skill.Contents = [new SkillContent { Lesson = lesson, SortOrder = -1 }]; break;
            case "duplicate-skill": db.Add(new SkillCategory { Name = "Constraint category", Skills = [new CategorySkillRef { Skill = skill, SortOrder = 0 }, new CategorySkillRef { Skill = skill, SortOrder = 1 }] }); break;
            case "invalid-icon": skill.IconKey = "invalid"; break;
        }
        db.Add(skill);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Inline_skill_publication_invalidates_an_open_category_editor()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = new SkillCategory { Name = "Inline publication category" };
        var revision = new SkillTreeRevision { Status = SkillTreeRevisionStatus.Published,
            Categories = [new SkillTreeCategoryRef { Category = category, SortOrder = 0 }] };
        var tree = new SkillTree { Name = "Inline publication tree", Revisions = [revision] };
        var lesson = new Lesson { Localizations = [new LessonLocalization { Locale = "en", Title = "Inline lesson", Body = "Content" }] };
        db.AddRange(tree, lesson); await db.SaveChangesAsync();
        tree.CurrentPublishedRevisionId = revision.Id; await db.SaveChangesAsync();
        var staleCategoryVersion = category.RowVersion;
        using var admin = factory.CreateClient();
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "SkillTree!123", role: Role.Admin);
        (await admin.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = user.UserName, Password = "SkillTree!123" })).EnsureSuccessStatusCode();
        var publish = await admin.PostAsJsonAsync($"/api/admin/lessons/{lesson.Id}/publish",
            new { rowVersion = lesson.RowVersion, inlineSkills = new[] { new { categoryId = category.Id, name = "Published inline skill", summary = "", iconKey = "web" } } });
        publish.EnsureSuccessStatusCode();
        var stale = await admin.PutAsJsonAsync($"/api/admin/skill-categories/{category.Id}/skills",
            new CategorySkillsUpdateCommand(staleCategoryVersion, []));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var current = (await admin.GetFromJsonAsync<SkillCategoryAdminResponse>($"/api/admin/skill-categories/{category.Id}"))!;
        Assert.NotEqual(staleCategoryVersion, current.RowVersion);
        Assert.Single(current.Skills!);
        Assert.Contains(current.Contents, content => content.ContentId == lesson.Id);
    }

}
