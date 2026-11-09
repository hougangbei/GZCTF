using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json;
using GZCTF.Features.SkillTrees.Api;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Features.LearningPaths.Domain;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.SkillTrees;

[Collection(nameof(IntegrationTestCollection))]
public class CourseTransferTests(GZCTFApplicationFactory factory)
{
    private async Task<HttpClient> Admin()
    {
        const string password = "T09!AdminPassword";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password, role: Role.Admin);
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = user.UserName, Password = password })).EnsureSuccessStatusCode();
        return client;
    }
    private async Task<(Guid tree, Guid category, Guid skill, JsonObject workspace)> Tree(HttpClient client, Guid lesson, bool include, Guid? sharedSkill = null)
    {
        var tree = new SkillTree { Name = "Transfer " + Guid.NewGuid() };
        var category = new SkillCategory { Name = "Original category" };
        var skill = new Skill { Id = sharedSkill ?? Guid.NewGuid(), Name = "Original skill" };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SkillTrees.Add(tree); db.SkillCategories.Add(category);
            if (sharedSkill is null) db.Skills.Add(skill);
            await db.SaveChangesAsync();
        }
        var path = $"/api/admin/skill-trees/{tree.Id}/workspace";
        var workspace = (await client.GetFromJsonAsync<JsonObject>(path))!;
        var document = new CourseDocument(tree.Name, "", "flag", null, [new(category.Id, "Course category", "", "flag", [new(skill.Id, "Course skill", "", "flag", true,
            include ? [new(lesson, "lesson", "Transfer lesson", "", "Draft")] : [])])]);
        var saved = await client.PutAsJsonAsync(path, new { version = workspace["version"]!.GetValue<string>(), document });
        saved.EnsureSuccessStatusCode();
        return (tree.Id, category.Id, skill.Id, (await saved.Content.ReadFromJsonAsync<JsonObject>())!);
    }
    [Fact]
    public async Task Move_is_atomic_idempotent_and_only_changes_drafts()
    {
        using var admin = await Admin();
        var lesson = new Lesson { Localizations = [new() { Locale = "en", Title = "Transfer lesson", Body = "private body sentinel" }] };
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Lessons.Add(lesson); await db.SaveChangesAsync(); }
        var source = await Tree(admin, lesson.Id, true); var target = await Tree(admin, lesson.Id, false);
        var operationId = Guid.NewGuid();
        object Command(string mode = "move", string? targetVersion = null) => new { operationId, mode, sourceTreeId = source.tree, sourceVersion = source.workspace["version"]!.GetValue<string>(), sourceCategoryId = source.category, sourceSkillId = source.skill,
            targetTreeId = target.tree, targetVersion = targetVersion ?? target.workspace["version"]!.GetValue<string>(), targetCategoryId = target.category, targetSkillId = target.skill, contents = new[] { new { kind = "lesson", contentId = lesson.Id } } };
        var injected = JsonSerializer.SerializeToNode(Command())!.AsObject(); injected["body"] = "untrusted private override";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/course-transfers", injected)).StatusCode);
        var oversized = JsonSerializer.SerializeToNode(Command())!.AsObject(); oversized["contents"] = JsonSerializer.SerializeToNode(Enumerable.Range(0, 101).Select(_ => new { kind = "lesson", contentId = lesson.Id }));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/course-transfers", oversized)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/course-transfers", Command(targetVersion: Guid.NewGuid().ToString()))).StatusCode);
        Assert.Contains(lesson.Id.ToString(), await admin.GetStringAsync($"/api/admin/skill-trees/{source.tree}/workspace"));
        var moved = await admin.PostAsJsonAsync("/api/admin/course-transfers", Command()); moved.EnsureSuccessStatusCode();
        var result = (await moved.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(1, result["addedCount"]!.GetValue<int>()); Assert.Equal(1, result["removedCount"]!.GetValue<int>());
        Assert.Empty(result["source"]!["document"]!["categories"]![0]!["skills"]![0]!["contents"]!.AsArray());
        Assert.DoesNotContain("private body sentinel", result.ToJsonString());
        var replay = await admin.PostAsJsonAsync("/api/admin/course-transfers", Command()); replay.EnsureSuccessStatusCode();
        Assert.Equal(result.ToJsonString(), (await replay.Content.ReadFromJsonAsync<JsonObject>())!.ToJsonString());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/course-transfers", Command("reference"))).StatusCode);
        var rows = (await admin.GetFromJsonAsync<JsonArray>($"/api/admin/course-resources?kind=lesson&resourceId={lesson.Id}"))!;
        Assert.Single(rows); Assert.False(rows[0]!["unassociated"]!.GetValue<bool>());
        Assert.Single(rows[0]!["locations"]!.AsArray()); Assert.Equal(target.tree, rows[0]!["locations"]![0]!["treeId"]!.GetValue<Guid>());
        await using var verify = factory.Services.CreateAsyncScope();
        var context = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("private body sentinel", (await context.Lessons.Include(l => l.Localizations).SingleAsync(l => l.Id == lesson.Id)).Localizations.Single().Body);
        Assert.False(await context.CoursePublishedVersions.AnyAsync(v => v.SkillTreeId == source.tree || v.SkillTreeId == target.tree));
        var audit = await context.AuditEvents.Where(a => a.Action == "course_workspace.transfer" && a.TargetId == source.tree.ToString() && a.Succeeded).FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.NotNull(audit.DetailsJson); var details = JsonNode.Parse(audit.DetailsJson)!;
        Assert.Equal("move", details["mode"]!.GetValue<string>()); Assert.Equal(source.tree, details["source"]!["treeId"]!.GetValue<Guid>());
        Assert.Equal(target.tree, details["target"]!["treeId"]!.GetValue<Guid>()); Assert.Equal("Course category", details["source"]!["categoryName"]!.GetValue<string>());
        Assert.DoesNotContain("private body sentinel", audit.DetailsJson);
    }
    [Fact]
    public async Task Same_skill_across_trees_is_independent_but_same_tree_is_noop()
    {
        using var admin = await Admin();
        var lesson = new Lesson { Localizations = [new() { Locale = "en", Title = "Shared identity" }] };
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Lessons.Add(lesson); await db.SaveChangesAsync(); }
        var source = await Tree(admin, lesson.Id, true); var target = await Tree(admin, lesson.Id, false, source.skill);
        object Command(bool self) => new { operationId = Guid.NewGuid(), mode = "move", sourceTreeId = source.tree, sourceVersion = source.workspace["version"]!.GetValue<string>(), sourceCategoryId = source.category, sourceSkillId = source.skill,
            targetTreeId = self ? source.tree : target.tree, targetVersion = (self ? source.workspace : target.workspace)["version"]!.GetValue<string>(), targetCategoryId = self ? source.category : target.category, targetSkillId = source.skill, contents = new[] { new { kind = "lesson", contentId = lesson.Id } } };
        var noop = await admin.PostAsJsonAsync("/api/admin/course-transfers", Command(true)); noop.EnsureSuccessStatusCode();
        Assert.True((await noop.Content.ReadFromJsonAsync<JsonObject>())!["noOp"]!.GetValue<bool>());
        var moved = await admin.PostAsJsonAsync("/api/admin/course-transfers", Command(false)); moved.EnsureSuccessStatusCode();
        Assert.Equal(1, (await moved.Content.ReadFromJsonAsync<JsonObject>())!["removedCount"]!.GetValue<int>());
    }
    [Theory]
    [InlineData("reference", false, 1, 0, 0)]
    [InlineData("reference", true, 0, 0, 1)]
    [InlineData("move", true, 0, 1, 1)]
    public async Task Duplicates_are_deduplicated_and_reference_preserves_source(string mode, bool duplicate, int added, int removed, int duplicates)
    {
        using var admin = await Admin();
        var lesson = new Lesson { Localizations = [new() { Locale = "en", Title = "Duplicate fixture" }] };
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Lessons.Add(lesson); await db.SaveChangesAsync(); }
        var source = await Tree(admin, lesson.Id, true); var target = await Tree(admin, lesson.Id, duplicate);
        var command = new { operationId = Guid.NewGuid(), mode, sourceTreeId = source.tree, sourceVersion = source.workspace["version"]!.GetValue<string>(), sourceCategoryId = source.category, sourceSkillId = source.skill,
            targetTreeId = target.tree, targetVersion = target.workspace["version"]!.GetValue<string>(), targetCategoryId = target.category, targetSkillId = target.skill, contents = new[] { new { kind = "lesson", contentId = lesson.Id } } };
        var result = await admin.PostAsJsonAsync("/api/admin/course-transfers", command); result.EnsureSuccessStatusCode();
        var json = (await result.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(added, json["addedCount"]!.GetValue<int>()); Assert.Equal(removed, json["removedCount"]!.GetValue<int>()); Assert.Equal(duplicates, json["duplicateCount"]!.GetValue<int>());
        Assert.Single(json["target"]!["document"]!["categories"]![0]!["skills"]![0]!["contents"]!.AsArray());
        if (mode == "reference") Assert.Single(json["source"]!["document"]!["categories"]![0]!["skills"]![0]!["contents"]!.AsArray());
    }
    [Fact]
    public async Task Shared_skill_occurrences_change_together_and_invalid_parent_or_retirement_rolls_back()
    {
        using var admin = await Admin();
        var lesson = new Lesson { Localizations = [new() { Locale = "en", Title = "Shared fixture" }] };
        var extra = new SkillCategory { Name = "Second occurrence" };
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Lessons.Add(lesson); db.SkillCategories.Add(extra); await db.SaveChangesAsync(); }
        var source = await Tree(admin, lesson.Id, true); var target = await Tree(admin, lesson.Id, false);
        var doc = source.workspace["document"]!.DeepClone(); var occurrence = doc["categories"]![0]!.DeepClone(); occurrence["categoryId"] = extra.Id;
        doc["categories"]!.AsArray().Add(occurrence);
        var save = await admin.PutAsJsonAsync($"/api/admin/skill-trees/{source.tree}/workspace", new { version = source.workspace["version"]!.GetValue<string>(), document = doc }); save.EnsureSuccessStatusCode();
        var sourceWorkspace = (await save.Content.ReadFromJsonAsync<JsonObject>())!;
        object Command(Guid sourceCategory) => new { operationId = Guid.NewGuid(), mode = "move", sourceTreeId = source.tree, sourceVersion = sourceWorkspace["version"]!.GetValue<string>(), sourceCategoryId = sourceCategory, sourceSkillId = source.skill,
            targetTreeId = target.tree, targetVersion = target.workspace["version"]!.GetValue<string>(), targetCategoryId = target.category, targetSkillId = target.skill, contents = new[] { new { kind = "lesson", contentId = lesson.Id } } };
        using var anonymous = factory.CreateClient();
        Assert.NotEqual(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/admin/course-transfers", Command(source.category))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/course-transfers", Command(target.category))).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Lessons.Where(l => l.Id == lesson.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.PublicationState, LessonPublicationState.Retired));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/course-transfers", Command(source.category))).StatusCode);
        Assert.Equal(sourceWorkspace["version"]!.GetValue<string>(), (await admin.GetFromJsonAsync<JsonObject>($"/api/admin/skill-trees/{source.tree}/workspace"))!["version"]!.GetValue<string>());
        await using (var scope = factory.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Lessons.Where(l => l.Id == lesson.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.PublicationState, LessonPublicationState.Draft));
        var moved = await admin.PostAsJsonAsync("/api/admin/course-transfers", Command(source.category)); moved.EnsureSuccessStatusCode();
        var result = (await moved.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.All(result["source"]!["document"]!["categories"]!.AsArray(), c => Assert.Empty(c!["skills"]![0]!["contents"]!.AsArray()));
    }

    [Fact]
    public async Task Transfer_preserves_published_payload_flags_and_learner_progress_identity()
    {
        using var admin = await Admin();
        const string password = "T09!LearnerPassword";
        var learner = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password);
        var lesson = new Lesson { Localizations = [new() { Locale = "en", Title = "Progress lesson", Body = "Frozen lesson" }] };
        var challenge = new GZCTF.Features.ChallengeLibrary.Domain.Challenge { SourceId = Guid.NewGuid().ToString(), Score = 321, Localizations = [new() { Locale = "en", Title = "Progress challenge", Body = "Frozen challenge" }],
            Flags = [new() { Kind = GZCTF.Features.ChallengeLibrary.Domain.ChallengeFlagKind.Static, Value = "flag{identity_preserved}" }] };
        var solve = new GZCTF.Features.LearningProgress.Domain.ChallengeProgress { UserId = learner.Id, Challenge = challenge, ImportedFrozenScore = 234 };
        var completed = new LessonProgress { UserId = learner.Id, Lesson = lesson };
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Lessons.Add(lesson); db.Challenges.Add(challenge); db.ChallengeProgress.Add(solve); db.LessonProgress.Add(completed); await db.SaveChangesAsync(); }
        var source = await Tree(admin, lesson.Id, true); var target = await Tree(admin, lesson.Id, false);
        var doc = source.workspace["document"]!.DeepClone();
        doc["categories"]![0]!["skills"]![0]!["contents"]!.AsArray().Add(JsonSerializer.SerializeToNode(new CourseContent(challenge.Id, "challenge", "Progress challenge", "", "Draft"), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var save = await admin.PutAsJsonAsync($"/api/admin/skill-trees/{source.tree}/workspace", new { version = source.workspace["version"]!.GetValue<string>(), document = doc }); save.EnsureSuccessStatusCode();
        var workspace = (await save.Content.ReadFromJsonAsync<JsonObject>())!;
        var publish = await admin.PostAsJsonAsync($"/api/admin/skill-trees/{source.tree}/workspace/publish", new { version = workspace["version"]!.GetValue<string>() }); publish.EnsureSuccessStatusCode();
        workspace = (await admin.GetFromJsonAsync<JsonObject>($"/api/admin/skill-trees/{source.tree}/workspace"))!;
        string snapshot; string[] payloads;
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); snapshot = await db.CoursePublishedVersions.Where(v => v.SkillTreeId == source.tree).Select(v => v.PublicDocumentJson).SingleAsync(); payloads = await db.CoursePublishedContents.Where(c => c.Version.SkillTreeId == source.tree).OrderBy(c => c.Kind).Select(c => c.PayloadJson).ToArrayAsync(); }
        var command = new { operationId = Guid.NewGuid(), mode = "move", sourceTreeId = source.tree, sourceVersion = workspace["version"]!.GetValue<string>(), sourceCategoryId = source.category, sourceSkillId = source.skill,
            targetTreeId = target.tree, targetVersion = target.workspace["version"]!.GetValue<string>(), targetCategoryId = target.category, targetSkillId = target.skill,
            contents = new[] { new { kind = "lesson", contentId = lesson.Id }, new { kind = "challenge", contentId = challenge.Id } } };
        (await admin.PostAsJsonAsync("/api/admin/course-transfers", command)).EnsureSuccessStatusCode();
        using var anonymous = factory.CreateClient();
        Assert.Contains("Progress challenge", await anonymous.GetStringAsync($"/api/skill-trees/{source.tree}"));
        var sourcePositions = (await admin.GetFromJsonAsync<JsonArray>($"/api/admin/course-resources?kind=challenge&resourceId={challenge.Id}"))!;
        Assert.Equal(2, sourcePositions[0]!["locations"]!.AsArray().Count);
        Assert.Contains(sourcePositions[0]!["locations"]!.AsArray(), l => l!["state"]!.GetValue<string>() == "published" && l["treeId"]!.GetValue<Guid>() == source.tree);
        await using var verification = factory.Services.CreateAsyncScope(); var context = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(snapshot, await context.CoursePublishedVersions.Where(v => v.SkillTreeId == source.tree).Select(v => v.PublicDocumentJson).SingleAsync());
        Assert.Equal(payloads, await context.CoursePublishedContents.Where(c => c.Version.SkillTreeId == source.tree).OrderBy(c => c.Kind).Select(c => c.PayloadJson).ToArrayAsync());
        var persisted = await context.ChallengeProgress.SingleAsync(p => p.UserId == learner.Id && p.ChallengeId == challenge.Id);
        Assert.Equal(solve.Id, persisted.Id); Assert.Equal(234, persisted.ImportedFrozenScore);
        Assert.Equal(completed.Id, (await context.LessonProgress.SingleAsync(p => p.UserId == learner.Id && p.LessonId == lesson.Id)).Id);
        Assert.Equal(321, (await context.Challenges.SingleAsync(c => c.Id == challenge.Id)).Score);
        Assert.Equal("flag{identity_preserved}", (await context.ChallengeFlags.SingleAsync(f => f.ChallengeId == challenge.Id)).Value);
    }
    [Fact]
    public async Task Opposing_moves_use_stable_lock_order_and_conflict_without_partial_changes()
    {
        using var admin = await Admin();
        var a = new Lesson { Localizations = [new() { Locale = "en", Title = "Opposing A" }] }; var b = new Lesson { Localizations = [new() { Locale = "en", Title = "Opposing B" }] };
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Lessons.AddRange(a, b); await db.SaveChangesAsync(); }
        var first = await Tree(admin, a.Id, true); var second = await Tree(admin, b.Id, true);
        object Command(bool reverse) { var s = reverse ? second : first; var t = reverse ? first : second;
            return new { operationId = Guid.NewGuid(), mode = "move", sourceTreeId = s.tree, sourceVersion = s.workspace["version"]!.GetValue<string>(), sourceCategoryId = s.category, sourceSkillId = s.skill,
                targetTreeId = t.tree, targetVersion = t.workspace["version"]!.GetValue<string>(), targetCategoryId = t.category, targetSkillId = t.skill, contents = new[] { new { kind = "lesson", contentId = reverse ? b.Id : a.Id } } }; }
        var responses = await Task.WhenAll(admin.PostAsJsonAsync("/api/admin/course-transfers", Command(false)), admin.PostAsJsonAsync("/api/admin/course-transfers", Command(true)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var left = (await admin.GetFromJsonAsync<JsonObject>($"/api/admin/skill-trees/{first.tree}/workspace"))!;
        var right = (await admin.GetFromJsonAsync<JsonObject>($"/api/admin/skill-trees/{second.tree}/workspace"))!;
        var countLeft = left["document"]!["categories"]![0]!["skills"]![0]!["contents"]!.AsArray().Count; var countRight = right["document"]!["categories"]![0]!["skills"]![0]!["contents"]!.AsArray().Count;
        Assert.Equal(2, countLeft + countRight); Assert.True(countLeft == 0 || countRight == 0);
    }

    [Fact]
    public async Task Unassociated_library_category_retains_content_count_and_source_labels_without_get_writes()
    {
        using var admin = await Admin();
        var category = new SkillCategory { Name = "Library original" }; var skill = new Skill { Name = "Library skill" };
        var lesson = new Lesson { Localizations = [new() { Locale = "en", Title = "Library lesson" }] };
        var batch = new GZCTF.Features.Imports.Domain.PlatformImportBatch { SourceInstance = "source-instance", Format = "CTFd", Title = "Fixture event", Fingerprint = Guid.NewGuid().ToString() };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.SkillCategories.Add(category); db.Skills.Add(skill); db.Lessons.Add(lesson);
            db.CategorySkillRefs.Add(new() { Category = category, Skill = skill }); db.SkillContents.Add(new() { Skill = skill, Lesson = lesson });
            db.PlatformImportBatches.Add(batch); db.PlatformImportMaps.Add(new() { Batch = batch, Kind = "category", SourceInstance = batch.SourceInstance, SourceId = Guid.NewGuid().ToString(), TargetId = category.Id }); await db.SaveChangesAsync();
        }
        var rows = (await admin.GetFromJsonAsync<JsonArray>($"/api/admin/course-resources?kind=category&unassociated=true&resourceId={category.Id}"))!;
        var row = Assert.Single(rows); Assert.Equal(1, row!["contentCount"]!.GetValue<int>()); Assert.Equal("Library original", row["name"]!.GetValue<string>());
        Assert.Equal("Fixture event [source-instance / CTFd]", Assert.Single(row["sourceLabels"]!.AsArray())!.GetValue<string>());
        Assert.True(row["unassociated"]!.GetValue<bool>()); Assert.Empty(row["locations"]!.AsArray());
    }

    [Fact]
    public async Task Legacy_current_published_hierarchy_remains_a_location_after_draft_removal()
    {
        using var admin = await Admin(); var tree = new SkillTree { Name = "Legacy current publication" }; var category = new SkillCategory { Name = "Legacy category" }; var skill = new Skill { Name = "Legacy skill" };
        var lesson = new Lesson { Localizations = [new() { Locale = "en", Title = "Legacy live lesson" }] }; var revision = new SkillTreeRevision { SkillTree = tree, Status = SkillTreeRevisionStatus.Published };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.SkillTrees.Add(tree); db.SkillTreeRevisions.Add(revision); db.SkillTreeCategoryRefs.Add(new() { Revision = revision, Category = category });
            db.CategorySkillRefs.Add(new() { Category = category, Skill = skill }); db.SkillContents.Add(new() { Skill = skill, Lesson = lesson }); await db.SaveChangesAsync(); tree.CurrentPublishedRevisionId = revision.Id;
            db.CourseAuthoringWorkspaces.Add(new() { SkillTree = tree, DocumentJson = JsonSerializer.Serialize(new CourseDocument("Edited legacy draft", "", "flag", null, []), new JsonSerializerOptions(JsonSerializerDefaults.Web)) }); await db.SaveChangesAsync();
        }
        var rows = (await admin.GetFromJsonAsync<JsonArray>($"/api/admin/course-resources?kind=lesson&resourceId={lesson.Id}"))!;
        var row = Assert.Single(rows); Assert.False(row!["unassociated"]!.GetValue<bool>());
        var location = Assert.Single(row["locations"]!.AsArray()); Assert.Equal("published", location!["state"]!.GetValue<string>()); Assert.Equal("Legacy current publication", location["treeName"]!.GetValue<string>());
    }
    [Fact]
    public async Task Journal_migration_upgrades_old_database_and_enforces_actor_scoped_operation_identity()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var existing = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var databaseName = "course_transfer_" + Guid.NewGuid().ToString("N");
        await using var admin = new Npgsql.NpgsqlConnection(existing.Database.GetConnectionString()); await admin.OpenAsync();
        await using (var create = new Npgsql.NpgsqlCommand($"CREATE DATABASE {databaseName}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var connection = new Npgsql.NpgsqlConnectionStringBuilder(existing.Database.GetConnectionString()) { Database = databaseName, Pooling = false };
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options);
            var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
            await migrator.MigrateAsync("20261107040000_AddCourseWorkspaceContents");
            var tree = new SkillTree { Name = "Pre-upgrade identity" }; db.SkillTrees.Add(tree); await db.SaveChangesAsync();
            await migrator.MigrateAsync();
            Assert.Equal("Pre-upgrade identity", (await db.SkillTrees.AsNoTracking().SingleAsync(t => t.Id == tree.Id)).Name);
            Assert.False(db.Database.HasPendingModelChanges());
            var actor = Guid.NewGuid(); var operation = Guid.NewGuid();
            db.CourseTransferOperations.Add(new() { ActorId = actor, OperationId = operation, RequestHash = new string('a', 64), ResponseJson = "{}" }); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            db.CourseTransferOperations.Add(new() { ActorId = Guid.NewGuid(), OperationId = operation, RequestHash = new string('b', 64), ResponseJson = "{}" }); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            db.CourseTransferOperations.Add(new() { ActorId = actor, OperationId = operation, RequestHash = new string('b', 64), ResponseJson = "{}" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally { await using var drop = new Npgsql.NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync(); }
    }

    [Fact]
    public async Task Target_shared_skill_content_limit_rejects_the_entire_move()
    {
        using var admin = await Admin(); var lesson = new Lesson { Localizations = [new() { Locale = "en", Title = "Overflow source" }] };
        var existing = Enumerable.Range(0, 50).Select(i => new Lesson { Localizations = [new() { Locale = "en", Title = "Target " + i }] }).ToArray();
        var categories = Enumerable.Range(0, 100).Select(i => new SkillCategory { Name = "Capacity " + i }).ToArray(); var skill = new Skill { Name = "Capacity shared skill" }; var tree = new SkillTree { Name = "Full target" };
        var document = new CourseDocument(tree.Name, "", "flag", null, categories.Select(c => new CourseCategory(c.Id, c.Name, "", "flag", [new(skill.Id, skill.Name, "", "flag", true,
            existing.Select(l => new CourseContent(l.Id, "lesson", "Target", "", "Draft")).ToList())])).ToList());
        var workspace = new CourseAuthoringWorkspace { SkillTree = tree, DocumentJson = JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Lessons.Add(lesson); db.Lessons.AddRange(existing); db.SkillCategories.AddRange(categories); db.Skills.Add(skill); db.SkillTrees.Add(tree); db.CourseAuthoringWorkspaces.Add(workspace); await db.SaveChangesAsync(); }
        var source = await Tree(admin, lesson.Id, true); var operation = Guid.NewGuid();
        var command = new { operationId = operation, mode = "move", sourceTreeId = source.tree, sourceVersion = source.workspace["version"]!.GetValue<string>(), sourceCategoryId = source.category, sourceSkillId = source.skill,
            targetTreeId = tree.Id, targetVersion = workspace.Version.ToString(), targetCategoryId = categories[0].Id, targetSkillId = skill.Id, contents = new[] { new { kind = "lesson", contentId = lesson.Id } } };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/course-transfers", command)).StatusCode);
        var current = (await admin.GetFromJsonAsync<JsonObject>($"/api/admin/skill-trees/{source.tree}/workspace"))!;
        Assert.Equal(source.workspace["version"]!.GetValue<string>(), current["version"]!.GetValue<string>()); Assert.Single(current["document"]!["categories"]![0]!["skills"]![0]!["contents"]!.AsArray());
        await using var verify = factory.Services.CreateAsyncScope(); var context = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(workspace.Version, (await context.CourseAuthoringWorkspaces.SingleAsync(w => w.SkillTreeId == tree.Id)).Version);
        Assert.False(await context.CourseTransferOperations.AnyAsync(o => o.OperationId == operation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batch_transfer_rehomes_shared_private_image_without_self_deadlock_and_preserves_assets(bool missingAsset)
    {
        using var admin = await Admin(); var first = new Lesson { Localizations = [new() { Locale = "en", Title = "Shared image A" }] }; var second = new Lesson { Localizations = [new() { Locale = "en", Title = "Shared image B" }] };
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Lessons.AddRange(first, second); await db.SaveChangesAsync(); }
        var source = await Tree(admin, first.Id, true); var target = await Tree(admin, first.Id, false);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("shared asset fixture"u8));
        var key = $"course-assets/{source.tree:N}/{hash}"; var targetKey = $"course-assets/{target.tree:N}/{hash}";
        var doc = source.workspace["document"]!.DeepClone(); doc["categories"]![0]!["skills"]![0]!["contents"]!.AsArray().Add(JsonSerializer.SerializeToNode(new CourseContent(second.Id, "lesson", "Shared image B", "", "Draft"), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var saved = await admin.PutAsJsonAsync($"/api/admin/skill-trees/{source.tree}/workspace", new { version = source.workspace["version"]!.GetValue<string>(), document = doc }); saved.EnsureSuccessStatusCode(); var workspace = (await saved.Content.ReadFromJsonAsync<JsonObject>())!;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var registry = scope.ServiceProvider.GetRequiredService<GZCTF.Features.SkillTrees.Application.CourseAssetRegistry>();
            await using var bytes = new MemoryStream("shared asset fixture"u8.ToArray()); if (!missingAsset) await registry.StoreAsync(key, hash, bytes, default);
            foreach (var lesson in new[] { first, second })
            {
                lesson.Localizations[0].Body = $"![shared](/api/admin/skill-trees/{source.tree}/workspace/assets/{hash}/shared.png)";
                db.CourseWorkspaceContents.Add(new() { SkillTreeId = source.tree, Kind = "lesson", ContentId = lesson.Id, PayloadJson = GZCTF.Features.SkillTrees.Application.CourseWorkspaceContentService.FreezeLesson(lesson) });
            }
            await db.SaveChangesAsync();
        }
        var command = new { operationId = Guid.NewGuid(), mode = "move", sourceTreeId = source.tree, sourceVersion = workspace["version"]!.GetValue<string>(), sourceCategoryId = source.category, sourceSkillId = source.skill,
            targetTreeId = target.tree, targetVersion = target.workspace["version"]!.GetValue<string>(), targetCategoryId = target.category, targetSkillId = target.skill,
            contents = new[] { new { kind = "lesson", contentId = first.Id }, new { kind = "lesson", contentId = second.Id } } };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var response = await admin.PostAsJsonAsync("/api/admin/course-transfers", command, timeout.Token);
        if (missingAsset)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var retained = (await admin.GetFromJsonAsync<JsonObject>($"/api/admin/skill-trees/{source.tree}/workspace"))!;
            Assert.Equal(workspace["version"]!.GetValue<string>(), retained["version"]!.GetValue<string>());
            Assert.Equal(2, retained["document"]!["categories"]![0]!["skills"]![0]!["contents"]!.AsArray().Count);
            await using var unchanged = factory.Services.CreateAsyncScope(); var unchangedDb = unchanged.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(target.workspace["version"]!.GetValue<string>(), (await unchangedDb.CourseAuthoringWorkspaces.SingleAsync(w => w.SkillTreeId == target.tree)).Version.ToString());
            Assert.False(await unchangedDb.CourseTransferOperations.AnyAsync(o => o.OperationId == command.operationId));
            return;
        }
        response.EnsureSuccessStatusCode();
        await using var verification = factory.Services.CreateAsyncScope(); var context = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        var copied = await context.CourseWorkspaceContents.Where(c => c.SkillTreeId == target.tree).ToArrayAsync(); Assert.Equal(2, copied.Length);
        Assert.All(copied, c => { Assert.Contains($"/api/admin/skill-trees/{target.tree}/workspace/assets/{hash}/", c.PayloadJson); Assert.DoesNotContain($"/api/admin/skill-trees/{source.tree}/workspace/assets/", c.PayloadJson); });
        await context.CourseStoredAssets.Where(a => a.StorageKey == targetKey).ExecuteUpdateAsync(s => s.SetProperty(a => a.LastTouchedUtc, DateTimeOffset.UtcNow.AddDays(-8)));
        await verification.ServiceProvider.GetRequiredService<GZCTF.Features.SkillTrees.Application.CourseAssetRegistry>().CollectAsync(DateTimeOffset.UtcNow, default);
        Assert.True(await verification.ServiceProvider.GetRequiredService<GZCTF.Storage.Interface.IBlobStorage>().ExistsAsync(targetKey));
    }

}
