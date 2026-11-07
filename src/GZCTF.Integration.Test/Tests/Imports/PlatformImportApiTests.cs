using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Features.Imports.Application;
using GZCTF.Features.Imports.Infrastructure;
using GZCTF.Features.SkillTrees.Application;
using GZCTF.Storage.Interface;
using GZCTF.Models;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Imports;

[Collection(nameof(IntegrationTestCollection))]
public class PlatformImportApiTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Preview_execution_is_draft_idempotent_and_keeps_container_supplementary_attachment()
    {
        using var admin = await AdminAsync();
        var instance = "gz-" + Guid.NewGuid().ToString("N");
        var archive = Package();
        using var previewResponse = await admin.PostAsync("/api/admin/platform-imports/preview", Upload(archive, instance));
        previewResponse.EnsureSuccessStatusCode();
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(preview.GetProperty("canImport").GetBoolean(), preview.GetProperty("issues").GetRawText());
        Assert.Equal(2, preview.GetProperty("challengeCount").GetInt32());
        var plan = preview.GetProperty("planFingerprint").GetString()!;
        using var importResponse = await admin.PostAsync("/api/admin/platform-imports", Upload(archive, instance, plan));
        importResponse.EnsureSuccessStatusCode();
        var result = await importResponse.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = result.GetProperty("id").GetGuid();
        var treeId = result.GetProperty("treeId").GetGuid();
        using var repeated = await admin.PostAsync("/api/admin/platform-imports", Upload(archive, instance, plan));
        repeated.EnsureSuccessStatusCode();
        Assert.Equal(batchId, (await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tree = await db.SkillTrees.Include(x => x.Revisions).ThenInclude(x => x.Categories).ThenInclude(x => x.Category).ThenInclude(x => x.Skills).ThenInclude(x => x.Skill).ThenInclude(x => x.Contents).ThenInclude(x => x.Challenge).SingleAsync(x => x.Id == treeId);
        Assert.Null(tree.CurrentPublishedRevisionId);
        Assert.Single(tree.Revisions.SelectMany(item => item.Categories));
        var challenges = tree.Revisions.SelectMany(x => x.Categories).SelectMany(x => x.Category.Skills).SelectMany(x => x.Skill.Contents).Select(x => x.Challenge!).ToArray();
        Assert.Equal(2, challenges.Length);
        Assert.All(challenges, x => Assert.Equal("Draft", x.PublicationState.ToString()));
        var container = challenges.Single(x => x.Type == ChallengeType.DynamicContainer);
        var runtime = JsonDocument.Parse(container.RuntimeConfigurationJson!).RootElement;
        Assert.Single(runtime.GetProperty("Attachments").EnumerateArray());
        Assert.Equal("gzctf-random", runtime.GetProperty("FlagMode").GetString());
        using var resources = await admin.GetAsync($"/api/challenges/{container.Id}");
        resources.EnsureSuccessStatusCode();
        Assert.True((await resources.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hasAttachment").GetBoolean());
        using var download = await admin.GetAsync($"/api/challenges/{container.Id}/attachment");
        download.EnsureSuccessStatusCode();
        Assert.Equal("supplement", await download.Content.ReadAsStringAsync());
        Assert.False(await db.UserChallengeInstances.AnyAsync(x => x.ChallengeId == container.Id));
    }

    [Fact]
    public async Task Import_requires_admin_and_the_confirmed_preview_plan()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/admin/platform-imports/preview", Upload(Package(), "unauthorized"))).StatusCode);
        using var admin = await AdminAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync("/api/admin/platform-imports", Upload(Package(), "stale-" + Guid.NewGuid().ToString("N"), "wrong"))).StatusCode);
        using var bad = new MultipartFormDataContent();
        bad.Add(new ByteArrayContent([1, 2, 3]), "package", "invalid.zip");
        bad.Add(new StringContent("{\"sourceInstance\":\"test\"}"), "options");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/platform-imports/preview", bad)).StatusCode);
    }
    [Fact]
    public async Task Import_keeps_zero_score_and_preflight_blocks_out_of_range_values()
    {
        using var admin = await AdminAsync();
        var source = "limits-" + Guid.NewGuid().ToString("N");
        var archive = Package(0);
        using var previewResponse = await admin.PostAsync("/api/admin/platform-imports/preview", Upload(archive, source));
        previewResponse.EnsureSuccessStatusCode();
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(preview.GetProperty("canImport").GetBoolean());
        using var response = await admin.PostAsync("/api/admin/platform-imports", Upload(archive, source, preview.GetProperty("planFingerprint").GetString()));
        response.EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.PlatformImportMaps.Where(item => item.SourceInstance == source && item.Kind == "challenge" && item.SourceId == "1").Select(item => item.TargetId).SingleAsync();
        Assert.Equal(0, (await db.Challenges.SingleAsync(item => item.Id == id)).Score);
        using var invalid = await admin.PostAsync("/api/admin/platform-imports/preview", Upload(Package(10001), "invalid-" + Guid.NewGuid().ToString("N")));
        invalid.EnsureSuccessStatusCode();
        var rejected = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(rejected.GetProperty("canImport").GetBoolean());
        Assert.Contains("score_out_of_range", rejected.GetProperty("issues").GetRawText());
    }
    [Fact]
    public async Task Failed_partial_blob_write_is_deleted_and_database_transaction_rolls_back()
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var inner = services.GetRequiredService<IBlobStorage>();
        var store = new FailingBlobStore(inner);
        var service = new PlatformImportService(db, services.GetRequiredService<ImportedAccountService>(), services.GetRequiredService<PlatformHistoryService>(), store, services.GetRequiredService<ISkillTreeCacheInvalidator>());
        await using var stream = new MemoryStream(Package());
        var package = await new PlatformArchiveReader().ReadAsync(stream);
        var options = new PlatformImportOptions("fail-" + Guid.NewGuid().ToString("N"));
        var preview = await service.PreviewAsync(package, options);
        await Assert.ThrowsAsync<IOException>(() => service.ImportAsync(package, options, preview.PlanFingerprint, Guid.NewGuid()));
        Assert.NotNull(store.WrittenKey);
        Assert.False(await inner.ExistsAsync(store.WrittenKey!));
        Assert.False(await db.PlatformImportBatches.AsNoTracking().AnyAsync(item => item.SourceInstance == options.SourceInstance));
    }
    [Fact]
    public async Task Blank_source_username_can_be_explicitly_overridden_or_skipped_after_preview()
    {
        using var admin = await AdminAsync();
        var archive = AccountPackage("   ", $"blank-{Guid.NewGuid():N}@example.test");
        var instance = "blank-" + Guid.NewGuid().ToString("N");
        MultipartFormDataContent Form(string? replacement = null, bool skip = false, string? plan = null)
        {
            var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(archive), "package", "fixture.zip");
            form.Add(new StringContent(JsonSerializer.Serialize(new PlatformImportOptions(instance,
                UserNameOverrides: replacement is null ? null : new Dictionary<string, string> { ["1"] = replacement },
                SkipUserIds: skip ? ["1"] : null))), "options");
            if (plan is not null) form.Add(new StringContent(plan), "planFingerprint");
            return form;
        }
        using var before = await admin.PostAsync("/api/admin/platform-imports/preview", Form());
        before.EnsureSuccessStatusCode();
        var invalid = await before.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(invalid.GetProperty("canImport").GetBoolean());
        Assert.Contains("invalid_user_name", invalid.GetProperty("issues").GetRawText());
        using var skipped = await admin.PostAsync("/api/admin/platform-imports/preview", Form(skip: true));
        skipped.EnsureSuccessStatusCode();
        Assert.True((await skipped.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("canImport").GetBoolean());
        var name = "b" + Guid.NewGuid().ToString("N")[..12];
        using var after = await admin.PostAsync("/api/admin/platform-imports/preview", Form(name));
        after.EnsureSuccessStatusCode();
        var valid = await after.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(valid.GetProperty("canImport").GetBoolean());
        using var executed = await admin.PostAsync("/api/admin/platform-imports", Form(name, plan: valid.GetProperty("planFingerprint").GetString()));
        executed.EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync(u => u.UserName == name));
    }
    private sealed class FailingBlobStore(IBlobStorage inner, Func<string, Task>? afterWrite = null) : IBlobStorage
    {
        public string? WrittenKey;
        public async Task WriteAsync(string path, Stream content, bool append = false, CancellationToken cancellationToken = default) { WrittenKey = path; await inner.WriteTextAsync(path, "partial", cancellationToken); if (afterWrite is null) throw new IOException("injected storage failure"); await afterWrite(path); }
        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) => inner.EnsureInitializedAsync(cancellationToken);
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => inner.ExistsAsync(path, cancellationToken);
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => inner.DeleteAsync(path, cancellationToken);
        public Task<StorageItem> GetBlobAsync(string path, CancellationToken cancellationToken = default) => inner.GetBlobAsync(path, cancellationToken);
        public Task<int> CountAsync(string path, CancellationToken cancellationToken = default) => inner.CountAsync(path, cancellationToken);
        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) => inner.OpenReadAsync(path, cancellationToken);
        public Task WriteFileAsync(string path, string localFilePath, CompressionFormat format = CompressionFormat.None, CancellationToken cancellationToken = default) => inner.WriteFileAsync(path, localFilePath, format, cancellationToken);
        public Task WriteTextAsync(string path, string contents, CancellationToken cancellationToken = default) => inner.WriteTextAsync(path, contents, cancellationToken);
        public Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default) => inner.ReadTextAsync(path, cancellationToken);
        public Task<IReadOnlyList<StorageItem>> ListAsync(string path, bool useFlatListing = false, CancellationToken cancellationToken = default) => inner.ListAsync(path, useFlatListing, cancellationToken);
    }
    [Fact]
    public async Task Concurrent_different_source_imports_cannot_create_two_accounts_with_one_email()
    {
        using var admin = await AdminAsync();
        var email = $"shared-{Guid.NewGuid():N}@example.test";
        var first = AccountPackage("a" + Guid.NewGuid().ToString("N")[..12], email);
        var second = AccountPackage("b" + Guid.NewGuid().ToString("N")[..12], email);
        var firstSource = "first-" + Guid.NewGuid().ToString("N");
        var secondSource = "second-" + Guid.NewGuid().ToString("N");
        async Task<string> Plan(byte[] archive, string source)
        { using var response = await admin.PostAsync("/api/admin/platform-imports/preview", Upload(archive, source)); response.EnsureSuccessStatusCode(); var preview = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(preview.GetProperty("canImport").GetBoolean()); return preview.GetProperty("planFingerprint").GetString()!; }
        var firstPlan = await Plan(first, firstSource);
        var secondPlan = await Plan(second, secondSource);
        var results = await Task.WhenAll(admin.PostAsync("/api/admin/platform-imports", Upload(first, firstSource, firstPlan)), admin.PostAsync("/api/admin/platform-imports", Upload(second, secondSource, secondPlan)));
        Assert.Single(results.Where(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Single(results.Where(response => response.StatusCode == HttpStatusCode.BadRequest));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(user => user.Email == email));
        foreach (var response in results)
            response.Dispose();
    }
    [Fact]
    public async Task Ctfd_case_insensitive_flag_survives_jsonb_and_fresh_HTTP_submission()
    {
        using var admin = await AdminAsync();
        var source = "case-" + Guid.NewGuid().ToString("N");
        var archive = AccountPackage("c" + Guid.NewGuid().ToString("N")[..12], $"case-{Guid.NewGuid():N}@example.test", true);
        using var pre = await admin.PostAsync("/api/admin/platform-imports/preview", Upload(archive, source));
        pre.EnsureSuccessStatusCode();
        var preview = await pre.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(preview.GetProperty("canImport").GetBoolean());
        using var imported = await admin.PostAsync("/api/admin/platform-imports", Upload(archive, source, preview.GetProperty("planFingerprint").GetString()));
        imported.EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.PlatformImportMaps.Where(item => item.SourceInstance == source && item.Kind == "challenge").Select(item => item.TargetId).SingleAsync();
        var treeId = await db.SkillTreeCategoryRefs.Where(r => r.Category.Skills.Any(s => s.Skill.Contents.Any(c => c.ChallengeId == id)))
            .Select(r => r.Revision.SkillTreeId).FirstAsync();
        var workspace = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/skill-trees/{treeId}/workspace");
        var publication = await admin.PostAsJsonAsync($"/api/admin/skill-trees/{treeId}/workspace/publish", new { version = workspace.GetProperty("version").GetString() });
        publication.EnsureSuccessStatusCode();
        using var response = await admin.PostAsJsonAsync($"/api/challenges/{id}/submissions", new { flag = "FLAG{ACCOUNT-FIXTURE}" });
        response.EnsureSuccessStatusCode();
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accepted").GetBoolean());
    }
    private static byte[] AccountPackage(string name, string email, bool ignoreCase = false)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            void Write(string table, object rows)
            { using var writer = new StreamWriter(zip.CreateEntry($"db/{table}.json").Open()); writer.Write(JsonSerializer.Serialize(new { results = rows })); }
            Write("config", new[] { new { key = "ctf_name", value = "Account import" } });
            Write("challenges", new[] { new { id = 1, name = "Zero-point task", description = "Description", category = "Web", type = "standard", state = "visible", value = 0 } });
            Write("flags", new[] { new { id = 1, challenge_id = 1, type = "static", content = "flag{account-fixture}", data = ignoreCase ? "case_insensitive" : (string?)null } });
            Write("users", new[] { new { id = 1, name, email, password = (string?)null, verified = true, banned = false, type = "admin" } });
        }
        return stream.ToArray();
    }
    [Fact]
    public async Task Target_changed_while_assets_stage_cannot_overwrite_the_approved_version()
    {
        Guid target;
        uint version;
        using (var seed = factory.Services.CreateScope())
        { var db = seed.ServiceProvider.GetRequiredService<AppDbContext>(); var skill = new GZCTF.Features.SkillTrees.Domain.Skill { Name = "Staging race" }; db.Skills.Add(skill); await db.SaveChangesAsync(); target = skill.Id; version = skill.RowVersion; }
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<AppDbContext>();
        var inner = services.GetRequiredService<IBlobStorage>();
        var store = new FailingBlobStore(inner, async _ => { using var external = factory.Services.CreateScope(); await external.ServiceProvider.GetRequiredService<AppDbContext>().Skills.Where(item => item.Id == target).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Summary, "Other admin edit").SetProperty(item => item.UpdatedAtUtc, DateTimeOffset.UtcNow)); });
        var service = new PlatformImportService(context, services.GetRequiredService<ImportedAccountService>(), services.GetRequiredService<PlatformHistoryService>(), store, services.GetRequiredService<ISkillTreeCacheInvalidator>());
        await using var input = new MemoryStream(Package());
        var package = await new PlatformArchiveReader().ReadAsync(input);
        var options = new PlatformImportOptions("race-" + Guid.NewGuid().ToString("N"), TargetSkillId: target, TargetSkillRowVersion: version);
        var preview = await service.PreviewAsync(package, options);
        await Assert.ThrowsAsync<PlatformImportPlanChangedException>(() => service.ImportAsync(package, options, preview.PlanFingerprint, Guid.NewGuid()));
        Assert.False(await inner.ExistsAsync(store.WrittenKey!));
        Assert.False(await context.SkillContents.AnyAsync(item => item.SkillId == target));
        Assert.Equal("Other admin edit", (await context.Skills.AsNoTracking().SingleAsync(item => item.Id == target)).Summary);
    }
    private async Task<HttpClient> AdminAsync()
    {
        var name = TestDataSeeder.RandomName();
        await TestDataSeeder.CreateUserAsync(factory.Services, name, "Import!12345", role: Role.Admin);
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = name, Password = "Import!12345" })).EnsureSuccessStatusCode();
        return client;
    }
    private static MultipartFormDataContent Upload(byte[] bytes, string instance, string? plan = null)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "package", "test.zip");
        content.Add(new StringContent(JsonSerializer.Serialize(new { sourceInstance = instance })), "options");
        if (plan != null)
            content.Add(new StringContent(plan), "planFingerprint");
        return content;
    }
    private static byte[] Package(int score = 100)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            void Write(string path, object value)
            { using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); writer.Write(JsonSerializer.Serialize(value)); }
            Write("manifest.json", new { version = "1.0", format = "GZCTF-GAME", game = new { id = 1, title = "导入测试" }, statistics = new { challengeCount = 2 } });
            Write("game.json", new { title = "导入测试", summary = "完整测试", content = "" });
            var file = Encoding.UTF8.GetBytes("supplement");
            var hash = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
            using (var output = zip.CreateEntry("files/" + hash).Open())
                output.Write(file);
            var common = new { image = "registry.invalid/challenge:1", exposePort = 8080, cpuCount = 1, memoryLimit = 128, storageLimit = 256, networkMode = "Open" };
            Write("challenges/challenge-1.json", new { id = 1, title = "静态题", content = "描述", category = "Web", difficultyLevel = "Medium", type = "StaticAttachment", enabled = true, scoring = new { original = score }, flags = new { @static = new[] { new { value = "flag{static}" } } }, hints = new[] { "提示" } });
            Write("challenges/challenge-2.json", new { id = 2, title = "容器附加附件", content = "描述", category = "Web", type = "DynamicContainer", enabled = false, scoring = new { original = 100 }, flags = new { template = (string?)null }, container = common, attachment = new { hash, fileName = "source.txt", fileSize = file.Length, type = "Local" } });
        }
        return stream.ToArray();
    }
}
