using GZCTF.Features.Imports.Application;
using GZCTF.Features.Imports.Domain;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Imports;
[Collection(nameof(IntegrationTestCollection))]
public class ImportCategoryGroupingTests(GZCTFApplicationFactory factory)
{
    private static PlatformImportPackage Package(string fingerprint) => new("CTFd", fingerprint, "Category grouping fixture",
        Enumerable.Range(1, 16).Select(i => new PlatformImportChallenge(i.ToString(), "Challenge " + i, "Body", i <= 13 ? "PWN" : "Pwn", ChallengeType.StaticAttachment, "Normal", 100, true, 0,
            [new("static", "flag{fixture}")], [], [], null, "{\"originalCategory\":\"" + (i <= 13 ? "PWN" : "Pwn") + "\"}")).ToArray(), [], [], [], [], new Dictionary<string, byte[]>(), [], "{}");
    [Fact]
    public async Task Fresh_package_casefolds_categories_without_cross_source_merge_and_preserves_reimport_identity()
    {
        var administrator = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "T09!ImportPassword", role: Role.Admin);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var service = scope.ServiceProvider.GetRequiredService<PlatformImportService>();
        var options = new PlatformImportOptions("casefold-" + Guid.NewGuid().ToString("N"), IncludeAccounts: false, IncludeHistory: false);
        var package = Package(Guid.NewGuid().ToString());
        var preview = await service.PreviewAsync(package, options);
        Assert.Contains(preview.Issues, i => i.Code == "category_casefold_merge" && i.Message.Contains("16"));
        var batch = await service.ImportAsync(package, options, preview.PlanFingerprint, administrator.Id);
        var categories = await db.SkillTreeCategoryRefs.Where(r => r.Revision.SkillTreeId == batch.TreeId).Select(r => r.Category).ToArrayAsync();
        var category = Assert.Single(categories); Assert.Equal("PWN", category.Name);
        var maps = await db.PlatformImportMaps.Where(m => m.SourceInstance == options.SourceInstance && m.Kind == "challenge").OrderBy(m => m.SourceId).Select(m => m.TargetId).ToArrayAsync();
        Assert.Equal(16, maps.Length);
        // A local organizer may move a resource out of the imported global skill; retry must not restore it.
        var removedRef = await db.SkillContents.FirstAsync(c => c.ChallengeId == maps[0]);
        db.SkillContents.Remove(removedRef); await db.SaveChangesAsync();
        var changed = package with { Fingerprint = Guid.NewGuid().ToString() };
        var again = await service.PreviewAsync(changed, options); await service.ImportAsync(changed, options, again.PlanFingerprint, administrator.Id);
        Assert.Equal(maps, await db.PlatformImportMaps.Where(m => m.SourceInstance == options.SourceInstance && m.Kind == "challenge").OrderBy(m => m.SourceId).Select(m => m.TargetId).ToArrayAsync());
        Assert.Equal(15, await db.SkillContents.CountAsync(c => maps.Contains(c.ChallengeId!.Value)));
        var otherOptions = options with { SourceInstance = "other-" + Guid.NewGuid().ToString("N") };
        var otherPreview = await service.PreviewAsync(package, otherOptions); var other = await service.ImportAsync(package, otherOptions, otherPreview.PlanFingerprint, administrator.Id);
        Assert.DoesNotContain(await db.SkillTreeCategoryRefs.Where(r => r.Revision.SkillTreeId == other.TreeId).Select(r => r.CategoryId).ToArrayAsync(), id => id == category.Id);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Legacy_exact_keys_win_and_partial_case_variant_maps_reuse_existing_category(bool hasSkillMap)
    {
        var administrator = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "T09!ImportPassword", role: Role.Admin);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var service = scope.ServiceProvider.GetRequiredService<PlatformImportService>();
        var source = "legacy-case-" + Guid.NewGuid().ToString("N");
        var category = new SkillCategory { Name = "Legacy PWN" }; var skill = new Skill { Name = "Legacy skill" };
        var batch = new PlatformImportBatch { SourceInstance = source, Format = "CTFd", Title = "Legacy import", Fingerprint = Guid.NewGuid().ToString() };
        var tree = new SkillTree { Name = "Legacy tree" }; var revision = new SkillTreeRevision { SkillTree = tree }; tree.Revisions.Add(revision);
        revision.Categories.Add(new() { Category = category }); db.SkillTrees.Add(tree); db.CategorySkillRefs.Add(new() { Category = category, Skill = skill }); db.PlatformImportBatches.Add(batch);
        string Key(string name) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("site:" + name)));
        foreach (var mapping in new[] { new PlatformImportMap { Kind = "tree", SourceId = "site", TargetId = tree.Id }, new() { Kind = "category", SourceId = Key("PWN"), TargetId = category.Id }, new() { Kind = "skill", SourceId = Key("PWN"), TargetId = skill.Id } })
        { if (!hasSkillMap && mapping.Kind == "skill") continue; mapping.Batch = batch; mapping.SourceInstance = source; db.PlatformImportMaps.Add(mapping); }
        await db.SaveChangesAsync();
        var options = new PlatformImportOptions(source, IncludeAccounts: false, IncludeHistory: false); var package = Package(Guid.NewGuid().ToString());
        var preview = await service.PreviewAsync(package, options); await service.ImportAsync(package, options, preview.PlanFingerprint, administrator.Id);
        var variantKey = Key("Pwn");
        var mappedSkillId = (await db.PlatformImportMaps.SingleAsync(m => m.SourceInstance == source && m.Kind == "skill" && m.SourceId == variantKey)).TargetId;
        if (hasSkillMap) Assert.Equal(skill.Id, mappedSkillId);
        Assert.Equal(category.Id, (await db.PlatformImportMaps.SingleAsync(m => m.SourceInstance == source && m.Kind == "category" && m.SourceId == variantKey)).TargetId);
        Assert.Equal(16, await db.SkillContents.CountAsync(c => c.SkillId == mappedSkillId));
        Assert.Equal("Legacy PWN", (await db.SkillCategories.SingleAsync(c => c.Id == category.Id)).Name);
    }

    [Fact]
    public async Task Whitespace_only_category_is_rejected_before_normalization()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var service = scope.ServiceProvider.GetRequiredService<PlatformImportService>();
        var package = Package(Guid.NewGuid().ToString()); package = package with { Challenges = [package.Challenges[0] with { Category = " \t " }] };
        var preview = await service.PreviewAsync(package, new("white-" + Guid.NewGuid().ToString("N"), IncludeAccounts: false, IncludeHistory: false));
        Assert.False(preview.CanImport); Assert.Contains(preview.Issues, i => i.Code == "invalid_category_name");
    }

}
