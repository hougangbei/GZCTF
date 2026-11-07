using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Xunit;
using SkiaSharp;
using GZCTF.Storage.Interface;
using GZCTF.Models;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Features.SkillTrees.Application;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.LearningPaths.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GZCTF.Integration.Test.Tests.SkillTrees;

[Collection(nameof(IntegrationTestCollection))]
public class CourseAuthoringWorkspaceTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Workspace_save_rejects_stale_version_and_does_not_publish_changes()
    {
        const string password = "S07!AdminPassword";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password, role: Role.Admin);
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = user.UserName, Password = password })).EnsureSuccessStatusCode();
        var created = await admin.PostAsJsonAsync("/api/admin/skill-trees", new { name = "Workspace original", summary = "", iconKey = "flag" });
        created.EnsureSuccessStatusCode();
        var treeId = (await created.Content.ReadFromJsonAsync<JsonObject>())!["skillTreeId"]!.GetValue<Guid>();
        var path = $"/api/admin/skill-trees/{treeId}/workspace";
        var loaded = await admin.GetFromJsonAsync<JsonObject>(path);
        Assert.NotNull(loaded);
        var version = loaded!["version"]!.GetValue<string>();
        var document = loaded["document"]!.DeepClone();
        document["name"] = "Workspace edited";
        var save = await admin.PutAsJsonAsync(path, new { version, document });
        save.EnsureSuccessStatusCode();
        var stale = await admin.PutAsJsonAsync(path, new { version, document });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/skill-trees/{treeId}")).StatusCode);
    }
    [Fact]
    public async Task Contextual_content_update_cannot_fall_through_to_live_library()
    {
        const string password = "S07!AdminPassword";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password, role: Role.Admin);
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = user.UserName, Password = password })).EnsureSuccessStatusCode();
        var created = await admin.PostAsJsonAsync("/api/admin/lessons", new { localizations = new[] { new { locale = "en", title = "Original", body = "Original body" } } });
        created.EnsureSuccessStatusCode();
        var lessonId = (await created.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        var update = await admin.PutAsJsonAsync($"/api/admin/lessons/{lessonId}?course={Guid.NewGuid()}",
            new { localizations = new[] { new { locale = "en", title = "Changed", body = "Changed body" } } });
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        var lesson = await admin.GetFromJsonAsync<JsonObject>($"/api/admin/lessons/{lessonId}");
        Assert.Equal("Original", lesson!["title"]!.GetValue<string>());
    }
    [Fact]
    public async Task Migration_freezes_existing_published_tree_metadata()
    {
        using var scope = factory.Services.CreateScope();
        var existing = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var databaseName = "course_snapshot_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(existing.Database.GetConnectionString());
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(existing.Database.GetConnectionString()) { Database = databaseName, Pooling = false };
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261102092045_AddReusableSkills");
            var treeId = Guid.NewGuid(); var revisionId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SkillTrees\" (\"Id\", \"Name\", \"Summary\", \"IconKey\", \"CreatedAtUtc\", \"UpdatedAtUtc\") VALUES ({treeId}, {"Original"}, {"Summary"}, {"web"}, {DateTimeOffset.UtcNow}, {DateTimeOffset.UtcNow})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SkillTreeRevisions\" (\"Id\", \"SkillTreeId\", \"Status\", \"CreatedAtUtc\") VALUES ({revisionId}, {treeId}, {1}, {DateTimeOffset.UtcNow})");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SkillTrees\" SET \"CurrentPublishedRevisionId\" = {revisionId} WHERE \"Id\" = {treeId}");
            var challenge = new Challenge { PublicationState = ChallengePublicationState.Published, IsEnabled = true,
                Localizations = [new ChallengeLocalization { Locale = "en", Title = "Frozen challenge", Body = "Challenge body" }],
                Flags = [new ChallengeFlag { Kind = ChallengeFlagKind.Static, Value = "secret-flag" }] };
            var lesson = new Lesson { PublicationState = LessonPublicationState.Published,
                Localizations = [new LessonLocalization { Locale = "en", Title = "Frozen lesson", Body = "Lesson body" }] };
            var category = new SkillCategory { Name = "Frozen category", IconKey = "web" };
            var skill = new Skill { Name = "Frozen skill", IconKey = "web", IsEnabled = true };
            db.Challenges.Add(challenge); db.Lessons.Add(lesson); db.SkillCategories.Add(category); db.Skills.Add(skill);
            db.CategorySkillRefs.Add(new CategorySkillRef { Category = category, Skill = skill, SortOrder = 0 });
            db.SkillContents.Add(new SkillContent { Skill = skill, Challenge = challenge, SortOrder = 0 });
            db.SkillContents.Add(new SkillContent { Skill = skill, Lesson = lesson, SortOrder = 1 });
            await db.SaveChangesAsync();
            var categoryRefId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SkillTreeCategoryRefs\" (\"Id\", \"RevisionId\", \"CategoryId\", \"SortOrder\") VALUES ({categoryRefId}, {revisionId}, {category.Id}, {0})");
            await migrator.MigrateAsync();
            await using var verify = new NpgsqlConnection(connection.ConnectionString);
            await verify.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT \"PublicDocumentJson\" FROM \"CoursePublishedVersions\" WHERE \"SkillTreeId\" = @id", verify);
            cmd.Parameters.AddWithValue("id", treeId);
            var snapshot = (string?)await cmd.ExecuteScalarAsync();
            Assert.NotNull(snapshot);
            Assert.Contains("Original", snapshot);
            Assert.Contains("Frozen category", snapshot);
            Assert.Contains("Frozen skill", snapshot);
            Assert.Contains("Frozen challenge", snapshot);
            Assert.Contains("Frozen lesson", snapshot);
            Assert.DoesNotContain("secret-flag", snapshot);
            await using var payloadCmd = new NpgsqlCommand("SELECT \"PayloadJson\" FROM \"CoursePublishedContents\" WHERE \"VersionId\" = @id AND \"Kind\" = 'challenge'", verify);
            payloadCmd.Parameters.AddWithValue("id", revisionId);
            var payload = (string?)await payloadCmd.ExecuteScalarAsync();
            Assert.NotNull(payload);
            Assert.Contains("secret-flag", payload);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SkillTrees\" SET \"Name\" = {"Changed after migration"} WHERE \"Id\" = {treeId}");
            var published = await new SkillTreeQueryService(db).GetPublishedAsync(treeId, CancellationToken.None);
            Assert.NotNull(published);
            Assert.Equal("Original", published!.Name);
            Assert.Equal("Frozen category", Assert.Single(published.Categories).Name);
            Assert.Equal("Frozen challenge", Assert.Single(published.Categories[0].Skills!).Contents[0].Title);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
    [Fact]
    public async Task Contextual_lesson_edit_is_isolated_and_rejects_stale_content_version()
    {
        const string password = "S07!AdminPassword";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password, role: Role.Admin);
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = user.UserName, Password = password })).EnsureSuccessStatusCode();
        var created = await admin.PostAsJsonAsync("/api/admin/skill-trees", new { name = "Course", summary = "", iconKey = "flag" });
        var treeId = (await created.Content.ReadFromJsonAsync<JsonObject>())!["skillTreeId"]!.GetValue<Guid>();
        var lessonResponse = await admin.PostAsJsonAsync("/api/admin/lessons", new { localizations = new[] { new { locale = "en", title = "Original", body = "Original body" } } });
        var lessonId = (await lessonResponse.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        Guid categoryId, skillId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var category = new SkillCategory { Name = "Category", IconKey = "web" };
            var skill = new Skill { Name = "Skill", IconKey = "web" };
            db.SkillCategories.Add(category); db.Skills.Add(skill); await db.SaveChangesAsync();
            categoryId = category.Id; skillId = skill.Id;
        }
        var path = $"/api/admin/skill-trees/{treeId}/workspace";
        var workspace = (await admin.GetFromJsonAsync<JsonObject>(path))!;
        var document = workspace["document"]!.DeepClone();
        document["categories"] = JsonSerializer.SerializeToNode(new[] { new { categoryId, name = "Category", summary = "", iconKey = "web", skills = new[] { new { skillId, name = "Skill", summary = "", iconKey = "web", isEnabled = true, contents = new[] { new { contentId = lessonId, kind = "lesson", title = "Original", summary = "", state = "Draft" } } } } } });
        (await admin.PutAsJsonAsync(path, new { version = workspace["version"]!.GetValue<string>(), document })).EnsureSuccessStatusCode();
        using var assetForm = new MultipartFormDataContent();
        assetForm.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("private image bytes")), "files", "diagram.png");
        var assetResponse = await admin.PostAsync(path + "/assets", assetForm); assetResponse.EnsureSuccessStatusCode();
        var asset = (await assetResponse.Content.ReadFromJsonAsync<JsonArray>())![0]!;
        var draftAssetUrl = asset["url"]!.GetValue<string>();
        using var publicClient = factory.CreateClient();
        Assert.NotEqual(HttpStatusCode.OK, (await publicClient.GetAsync(draftAssetUrl)).StatusCode);
        using var libraryForm = new MultipartFormDataContent();
        libraryForm.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("library asset bytes")), "files", "library.png");
        var libraryAssetResponse = await admin.PostAsync($"/api/admin/content-assets?kind=lesson&contentId={lessonId}", libraryForm);
        libraryAssetResponse.EnsureSuccessStatusCode();
        var libraryAsset = (await libraryAssetResponse.Content.ReadFromJsonAsync<JsonArray>())![0]!;
        var libraryAssetUrl = libraryAsset["url"]!.GetValue<string>();
        Assert.NotEqual(HttpStatusCode.OK, (await publicClient.GetAsync(libraryAssetUrl)).StatusCode);
        var scopedPath = $"/api/admin/lessons/{lessonId}?course={treeId}&locale=zh";
        var scoped = (await admin.GetFromJsonAsync<JsonObject>(scopedPath))!;
        var version = scoped["publication"]!["rowVersion"]!.GetValue<uint>();
        var edit = new { rowVersion = version, locale = "zh", localizations = new[] { new { locale = "en", title = "Original", body = "Original body" }, new { locale = "zh", title = "Draft title", body = $"Draft body ![image]({draftAssetUrl}) ![library]({libraryAssetUrl})" } } };
        (await admin.PutAsJsonAsync(scopedPath, edit)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(scopedPath, edit)).StatusCode);
        Assert.Equal("Original", (await admin.GetFromJsonAsync<JsonObject>($"/api/admin/lessons/{lessonId}"))!["title"]!.GetValue<string>());
        Assert.Contains("Draft body", (await admin.GetFromJsonAsync<JsonObject>(scopedPath))!["body"]!.GetValue<string>());
        var savedWorkspace = (await admin.GetFromJsonAsync<JsonObject>(path))!;
        var savedVersion = savedWorkspace["version"]!.GetValue<string>();
        var check = await admin.PostAsJsonAsync(path + "/check", new { version = savedVersion });
        check.EnsureSuccessStatusCode();
        var report = (await check.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(1, report["lessonCount"]!.GetValue<int>());
        var publish = await admin.PostAsJsonAsync(path + "/publish", new { version = savedVersion });
        publish.EnsureSuccessStatusCode();
        using var anonymous = factory.CreateClient();
        var publicTree = (await anonymous.GetFromJsonAsync<JsonObject>($"/api/skill-trees/{treeId}"))!;
        Assert.Contains("Draft title", publicTree.ToJsonString());
        Assert.DoesNotContain("Draft body", publicTree.ToJsonString());
        var lessonDetail = (await anonymous.GetFromJsonAsync<JsonObject>($"/api/learning-lessons/{lessonId}?course={treeId}&locale=zh"))!;
        var publicBody = lessonDetail["body"]!.GetValue<string>();
        var publicAssetUrl = publicBody[(publicBody.IndexOf("/api/course-assets/", StringComparison.Ordinal))..].Split(')')[0];
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(publicAssetUrl)).StatusCode);
        var libraryPublicUrl = publicBody[(publicBody.IndexOf("/api/content-assets/", StringComparison.Ordinal))..].TrimEnd(')');
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(libraryPublicUrl)).StatusCode);
        Assert.DoesNotContain("/api/admin/", publicBody);
        var secondTreeResponse = await admin.PostAsJsonAsync("/api/admin/skill-trees", new { name = "Reuse", summary = "", iconKey = "flag" });
        var secondTreeId = (await secondTreeResponse.Content.ReadFromJsonAsync<JsonObject>())!["skillTreeId"]!.GetValue<Guid>();
        var secondPath = $"/api/admin/skill-trees/{secondTreeId}/workspace";
        var secondWorkspace = (await admin.GetFromJsonAsync<JsonObject>(secondPath))!;
        var secondDocument = savedWorkspace["document"]!.DeepClone(); secondDocument["name"] = "Reuse";
        (await admin.PutAsJsonAsync(secondPath, new { version = secondWorkspace["version"]!.GetValue<string>(), document = secondDocument })).EnsureSuccessStatusCode();
        var assetHash = asset["hash"]!.GetValue<string>(); var reusedKey = $"course-assets/{secondTreeId:N}/{assetHash}";
        await using (var reuseScope = factory.Services.CreateAsyncScope())
        {
            var registry = reuseScope.ServiceProvider.GetRequiredService<CourseAssetRegistry>();
            await using var bytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("private image bytes"));
            await registry.StoreAsync(reusedKey, assetHash, bytes, default);
            var reuseDb = reuseScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await reuseDb.CourseStoredAssets.Where(a => a.StorageKey == reusedKey).ExecuteUpdateAsync(s => s.SetProperty(a => a.LastTouchedUtc, DateTimeOffset.UtcNow.AddDays(-8)));
        }
        (await admin.GetAsync($"/api/admin/lessons/{lessonId}?course={secondTreeId}&locale=zh")).EnsureSuccessStatusCode();
        await using (var reuseScope = factory.Services.CreateAsyncScope())
        {
            var reuseDb = reuseScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True((await reuseDb.CourseStoredAssets.SingleAsync(a => a.StorageKey == reusedKey)).LastTouchedUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
        }
        var currentEdit = (await admin.GetFromJsonAsync<JsonObject>(scopedPath))!;
        var nextContentVersion = currentEdit["publication"]!["rowVersion"]!.GetValue<uint>();
        var missingHash = new string('a', 64);
        var broken = new { rowVersion = nextContentVersion, localizations = new[] { new { locale = "en", title = "Broken", body = $"![missing](/api/admin/skill-trees/{treeId}/workspace/assets/{missingHash}/missing.png)" } } };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(scopedPath, broken)).StatusCode);
        using var lostForm = new MultipartFormDataContent();
        lostForm.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("file later lost")), "files", "lost.png");
        var lostResponse = await admin.PostAsync(path + "/assets", lostForm); lostResponse.EnsureSuccessStatusCode();
        var lost = (await lostResponse.Content.ReadFromJsonAsync<JsonArray>())![0]!;
        var lostUrl = lost["url"]!.GetValue<string>(); var lostKey = lost["storageKey"]!.GetValue<string>();
        (await admin.PutAsJsonAsync(scopedPath, new { rowVersion = nextContentVersion, localizations = new[] { new { locale = "en", title = "Broken", body = $"![missing]({lostUrl})" } } })).EnsureSuccessStatusCode();
        await using (var lostScope = factory.Services.CreateAsyncScope())
            await lostScope.ServiceProvider.GetRequiredService<IBlobStorage>().DeleteAsync(lostKey);
        var nextWorkspace = (await admin.GetFromJsonAsync<JsonObject>(path))!;
        var nextVersion = nextWorkspace["version"]!.GetValue<string>();
        var brokenCheck = (await (await admin.PostAsJsonAsync(path + "/check", new { version = nextVersion })).Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Contains("content_asset_unreadable", brokenCheck.ToJsonString());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(path + "/publish", new { version = nextVersion })).StatusCode);
        Assert.Contains("Draft title", await anonymous.GetStringAsync($"/api/skill-trees/{treeId}"));
    }
    [Fact]
    public async Task Shared_challenge_runtime_change_is_blocked_and_other_course_stays_published()
    {
        Guid firstId, secondId, challengeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var challenge = new Challenge { PublicationState = ChallengePublicationState.Published, IsEnabled = true, Score = 100,
                Localizations = [new ChallengeLocalization { Locale = "en", Title = "Shared", Body = "Description" }],
                Flags = [new ChallengeFlag { Kind = ChallengeFlagKind.Static, Value = "flag{old}" }] };
            var category = new SkillCategory { Name = "Shared category", IconKey = "web" };
            var skill = new Skill { Name = "Shared skill", IconKey = "web" };
            db.CategorySkillRefs.Add(new CategorySkillRef { Category = category, Skill = skill });
            db.SkillContents.Add(new SkillContent { Skill = skill, Challenge = challenge });
            var first = new SkillTree { Name = "First", IconKey = "web" };
            var second = new SkillTree { Name = "Second", IconKey = "web" };
            var revisionA = new SkillTreeRevision { SkillTree = first, Status = SkillTreeRevisionStatus.Published };
            var revisionB = new SkillTreeRevision { SkillTree = second, Status = SkillTreeRevisionStatus.Published };
            db.SkillTreeCategoryRefs.Add(new SkillTreeCategoryRef { Revision = revisionA, Category = category });
            db.SkillTreeCategoryRefs.Add(new SkillTreeCategoryRef { Revision = revisionB, Category = category });
            db.SkillTrees.AddRange(first, second); await db.SaveChangesAsync();
            first.CurrentPublishedRevisionId = revisionA.Id; second.CurrentPublishedRevisionId = revisionB.Id;
            await db.SaveChangesAsync();
            await CourseSnapshotBuilder.CaptureCurrentAsync(db, first.Id, revisionA.Id, CancellationToken.None);
            await CourseSnapshotBuilder.CaptureCurrentAsync(db, second.Id, revisionB.Id, CancellationToken.None);
            await db.SaveChangesAsync();
            firstId = first.Id; secondId = second.Id; challengeId = challenge.Id;
        }
        const string password = "S07!AdminPassword";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password, role: Role.Admin);
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = user.UserName, Password = password })).EnsureSuccessStatusCode();
        var path = $"/api/admin/skill-trees/{firstId}/workspace";
        (await admin.GetAsync(path)).EnsureSuccessStatusCode();
        var editPath = $"/api/admin/challenges/{challengeId}/edit?course={firstId}";
        var edit = (await admin.GetFromJsonAsync<JsonObject>(editPath))!;
        var rowVersion = edit["challenge"]!["rowVersion"]!.GetValue<uint>();
        (await admin.PutAsJsonAsync($"/api/admin/challenges/{challengeId}?course={firstId}", new { rowVersion, score = 200 })).EnsureSuccessStatusCode();
        var workspace = (await admin.GetFromJsonAsync<JsonObject>(path))!;
        var version = workspace["version"]!.GetValue<string>();
        var check = (await (await admin.PostAsJsonAsync(path + "/check", new { version })).Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Contains("shared_challenge_conflict", check.ToJsonString());
        Assert.Contains(secondId.ToString(), check.ToJsonString());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(path + "/publish", new { version })).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Contains("Shared", await anonymous.GetStringAsync($"/api/skill-trees/{secondId}"));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Challenges.Where(c => c.Id == challengeId).ExecuteUpdateAsync(setters => setters.SetProperty(c => c.PublicationState, ChallengePublicationState.Merged));
        }
        var mergedCheck = (await (await admin.PostAsJsonAsync(path + "/check", new { version })).Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Contains("challenge_identity_unavailable", mergedCheck.ToJsonString());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(path + "/publish", new { version })).StatusCode);
    }
    [Fact]
    public async Task Draft_cover_is_private_until_the_workspace_is_published()
    {
        const string password = "S07!AdminPassword";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password, role: Role.Admin);
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = user.UserName, Password = password })).EnsureSuccessStatusCode();
        var created = await admin.PostAsJsonAsync("/api/admin/skill-trees", new { name = "Cover", summary = "", iconKey = "flag" });
        var treeId = (await created.Content.ReadFromJsonAsync<JsonObject>())!["skillTreeId"]!.GetValue<Guid>();
        var path = $"/api/admin/skill-trees/{treeId}/workspace";
        var workspace = (await admin.GetFromJsonAsync<JsonObject>(path))!;
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(workspace["version"]!.GetValue<string>()), "version");
        using var bitmap = new SKBitmap(16, 16); bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        form.Add(new ByteArrayContent(encoded.ToArray()), "file", "cover.png");
        var uploaded = await admin.PostAsync(path + "/cover", form);
        uploaded.EnsureSuccessStatusCode();
        var draft = (await uploaded.Content.ReadFromJsonAsync<JsonObject>())!;
        var draftUrl = draft["document"]!["coverUrl"]!.GetValue<string>();
        using var anonymous = factory.CreateClient();
        Assert.NotEqual(HttpStatusCode.OK, (await anonymous.GetAsync(draftUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(draftUrl)).StatusCode);
        (await admin.PostAsJsonAsync(path + "/publish", new { version = draft["version"]!.GetValue<string>() })).EnsureSuccessStatusCode();
        var detail = (await anonymous.GetFromJsonAsync<JsonObject>($"/api/skill-trees/{treeId}"))!;
        var publicUrl = detail["coverUrl"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(publicUrl)).StatusCode);
    }
    [Fact]
    public async Task Retrying_node_creation_reuses_identity_and_rejects_different_payload()
    {
        const string password = "S07!AdminPassword";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password, role: Role.Admin);
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = user.UserName, Password = password })).EnsureSuccessStatusCode();
        var created = await admin.PostAsJsonAsync("/api/admin/skill-trees", new { name = "Idempotency", summary = "", iconKey = "flag" });
        var treeId = (await created.Content.ReadFromJsonAsync<JsonObject>())!["skillTreeId"]!.GetValue<Guid>();
        var path = $"/api/admin/skill-trees/{treeId}/workspace";
        (await admin.GetAsync(path)).EnsureSuccessStatusCode();
        var operationId = Guid.NewGuid(); var command = new { operationId, kind = "skill", name = "One skill" };
        var first = await admin.PostAsJsonAsync(path + "/nodes", command); first.EnsureSuccessStatusCode();
        var second = await admin.PostAsJsonAsync(path + "/nodes", command); second.EnsureSuccessStatusCode();
        var id = (await first.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        Assert.Equal(id, (await second.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(path + "/nodes", new { operationId, kind = "skill", name = "Different" })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Skills.CountAsync(s => s.Id == id));
    }
    [Fact]
    public async Task Cleanup_removes_only_old_unreferenced_private_assets()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var registry = scope.ServiceProvider.GetRequiredService<CourseAssetRegistry>();
        var storage = scope.ServiceProvider.GetRequiredService<IBlobStorage>();
        var tree = new SkillTree { Name = "GC course" };
        var revision = new SkillTreeRevision { SkillTree = tree, Status = SkillTreeRevisionStatus.Published };
        db.SkillTrees.Add(tree); db.SkillTreeRevisions.Add(revision); await db.SaveChangesAsync();
        tree.CurrentPublishedRevisionId = revision.Id; await db.SaveChangesAsync();
        string NewHash() => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var unusedHash = NewHash(); var draftHash = NewHash();
        var publishedHash = NewHash(); var recentHash = NewHash();
        string Key(string hash) => $"course-assets/{tree.Id:N}/{hash}";
        foreach (var hash in new[] { unusedHash, draftHash, publishedHash, recentHash })
        {
            await using var bytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(hash));
            await registry.StoreAsync(Key(hash), hash, bytes, default);
        }
        db.CourseAuthoringWorkspaces.Add(new CourseAuthoringWorkspace { SkillTreeId = tree.Id, DocumentJson = draftHash });
        var snapshot = new CoursePublishedVersion { Id = revision.Id, SkillTreeId = tree.Id, PublicDocumentJson = "{}", PublishedAtUtc = DateTimeOffset.UtcNow };
        snapshot.Contents.Add(new CoursePublishedContent { Id = Guid.NewGuid(), VersionId = revision.Id, ContentId = Guid.NewGuid(), Kind = "lesson", PayloadJson = publishedHash });
        db.CoursePublishedVersions.Add(snapshot); await db.SaveChangesAsync();
        var recentKey = Key(recentHash); var treeKey = tree.Id.ToString("N");
        await db.CourseStoredAssets.Where(a => a.StorageKey != recentKey && a.StorageKey.Contains(treeKey))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastTouchedUtc, DateTimeOffset.UtcNow.AddDays(-8)));
        await registry.CollectAsync(DateTimeOffset.UtcNow, default);
        Assert.False(await storage.ExistsAsync(Key(unusedHash)));
        Assert.True(await storage.ExistsAsync(Key(draftHash)));
        Assert.True(await storage.ExistsAsync(Key(publishedHash)));
        Assert.True(await storage.ExistsAsync(Key(recentHash)));
    }
    [Fact]
    public async Task Cleanup_waits_for_inflight_asset_reference_and_preserves_it()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var registry = scope.ServiceProvider.GetRequiredService<CourseAssetRegistry>();
        var storage = scope.ServiceProvider.GetRequiredService<IBlobStorage>();
        var tree = new SkillTree { Name = "GC race" };
        db.SkillTrees.Add(tree); db.CourseAuthoringWorkspaces.Add(new CourseAuthoringWorkspace { SkillTree = tree, DocumentJson = "{}" });
        await db.SaveChangesAsync();
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)); var key = $"course-assets/{tree.Id:N}/{hash}";
        await using (var bytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("race"))) await registry.StoreAsync(key, hash, bytes, default);
        await db.CourseStoredAssets.Where(a => a.StorageKey == key).ExecuteUpdateAsync(s => s.SetProperty(a => a.LastTouchedUtc, DateTimeOffset.UtcNow.AddDays(-8)));
        await using var transaction = await db.Database.BeginTransactionAsync();
        await registry.LockReferencedAssetsAsync(db, key, default);
        var cleanup = registry.CollectAsync(DateTimeOffset.UtcNow, default);
        await Task.Delay(100);
        Assert.False(cleanup.IsCompleted);
        db.CourseWorkspaceContents.Add(new CourseWorkspaceContent { SkillTreeId = tree.Id, ContentId = Guid.NewGuid(), Kind = "lesson", PayloadJson = key });
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        await cleanup;
        Assert.True(await storage.ExistsAsync(key));
    }
}
