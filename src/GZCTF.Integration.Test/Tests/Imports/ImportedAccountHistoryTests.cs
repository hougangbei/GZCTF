using System.Text.Json;
using GZCTF.Features.Imports.Application;
using GZCTF.Features.Imports.Domain;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Utils;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Imports;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ImportedAccountHistoryTests(GZCTFApplicationFactory factory)
{
    private const string LegacyHash = "$bcrypt-sha256$2b,4$n79VH.0Q2TMWmt3Oqt9uku$218k5nCh3D4YqdpX.Zv79C4bjOSjWea";

    [Fact]
    public async Task Imported_password_rehashes_once_roles_are_safe_and_reimport_never_overwrites_credentials()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<UserInfo>>();
        var accounts = new ImportedAccountService(db, manager);
        var name = Name();
        var package = Package([User("1", name) with { MetadataJson = "{\"type\":\"admin\"}" }, User("2", Name()) with { Banned = true }]);
        var options = new PlatformImportOptions("accounts-" + Guid.NewGuid().ToString("N"));
        var batch = Batch(options);
        db.PlatformImportBatches.Add(batch);
        await using var transaction = await db.Database.BeginTransactionAsync();
        Assert.Empty((await accounts.PreviewAsync(package, options)).Where(i => i.Severity == "error"));
        Assert.Equal(2, await accounts.ImportAsync(package, options, batch));
        var user = await manager.FindByNameAsync(name);
        Assert.NotNull(user);
        Assert.Equal(Role.User, user.Role);
        Assert.True(user.EmailConfirmed);
        Assert.StartsWith("ctfd:", user.PasswordHash!);
        Assert.True(await manager.CheckPasswordAsync(user, "password"));
        Assert.DoesNotContain("ctfd:", user.PasswordHash!);
        var nativeHash = user.PasswordHash;
        var banned = await manager.FindByNameAsync(package.Users[1].UserName);
        Assert.Equal(Role.Banned, banned!.Role);
        var changed = package with { Users = [package.Users[0] with { PasswordHash = null, Banned = true, Verified = false }] };
        Assert.Equal(0, await accounts.ImportAsync(changed, options, batch));
        Assert.Equal(nativeHash, user.PasswordHash);
        Assert.Equal(Role.User, user.Role);
        Assert.True(user.EmailConfirmed);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Collisions_block_without_merging_and_unsupported_password_requires_reset()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<UserInfo>>();
        var accounts = new ImportedAccountService(db, manager);
        var native = new UserInfo { UserName = Name(), Email = Name() + "@example.test", EmailConfirmed = true, Role = Role.Admin };
        Assert.True((await manager.CreateAsync(native, "Existing!123")).Succeeded);
        var options = new PlatformImportOptions("collision-" + Guid.NewGuid().ToString("N"));
        var collision = Package([User("1", native.UserName!) with { Email = native.Email }]);
        var issues = await accounts.PreviewAsync(collision, options);
        Assert.Contains(issues, i => i.Code == "duplicate_user_name" && i.Severity == "error");
        Assert.Contains(issues, i => i.Code == "duplicate_user_email" && i.Severity == "error");
        var batch = Batch(options);
        db.PlatformImportBatches.Add(batch);
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.ImportAsync(collision, options, batch));
        Assert.True(await manager.CheckPasswordAsync(native, "Existing!123"));
        Assert.Equal(Role.Admin, native.Role);
        var resetSource = User("reset", Name()) with { PasswordHash = "$unknown$do-not-use" };
        var resetPackage = Package([resetSource]);
        Assert.Contains(await accounts.PreviewAsync(resetPackage, options), i => i.Code == "password_reset_required" && i.Severity == "warning");
        Assert.Equal(1, await accounts.ImportAsync(resetPackage, options, batch));
        var resetUser = await manager.FindByNameAsync(resetSource.UserName);
        Assert.NotNull(resetUser);
        Assert.Null(resetUser.PasswordHash);
        Assert.False(await manager.CheckPasswordAsync(resetUser, "password"));
        var token = await manager.GeneratePasswordResetTokenAsync(resetUser);
        Assert.True((await manager.ResetPasswordAsync(resetUser, token, "NewSecret!123")).Succeeded);
        Assert.True(await manager.CheckPasswordAsync(resetUser, "NewSecret!123"));
        Assert.False(await manager.CheckPasswordAsync(resetUser, "password"));
        Assert.DoesNotContain("ctfd:", resetUser.PasswordHash!);
    }

    [Fact]
    public async Task Explicit_skip_resolves_source_admin_collision_without_linking_or_modifying_native_account()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<UserInfo>>();
        var accounts = new ImportedAccountService(db, manager);
        var history = new PlatformHistoryService(db, scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
        var native = new UserInfo { UserName = Name(), Email = Name() + "@example.test", EmailConfirmed = true, Role = Role.Admin };
        Assert.True((await manager.CreateAsync(native, "NativeAdmin!123")).Succeeded);
        var skipped = User("source-admin", native.UserName!) with { Email = native.Email };
        var other = User("other", Name());
        var package = Package([skipped, other]);
        var options = new PlatformImportOptions("skipped-" + Guid.NewGuid().ToString("N"), SkipUserIds: [skipped.SourceId]);
        var issues = await accounts.PreviewAsync(package, options);
        Assert.DoesNotContain(issues, i => i.Severity == "error");
        Assert.Contains(issues, i => i.Code == "skipped_source_user" && i.SourceId == skipped.SourceId);
        var batch = Batch(options); db.PlatformImportBatches.Add(batch);
        Assert.Equal(1, await accounts.ImportAsync(package, options, batch));
        await history.ImportAsync(package, options, batch);
        Assert.False(await db.PlatformImportMaps.AnyAsync(m => m.SourceInstance == options.SourceInstance && m.SourceId == skipped.SourceId));
        var skippedMember = await db.PlatformHistoryMembers.SingleAsync(m => m.BatchId == batch.Id && m.SourceUserId == skipped.SourceId);
        Assert.Null(skippedMember.UserId);
        Assert.DoesNotContain((await history.GetAsync(native.Id, false)).Members!, m => m.BatchId == batch.Id);
        Assert.Contains((await history.GetAsync(native.Id, true)).Members!, m => m.Id == skippedMember.Id);
        Assert.True(await manager.CheckPasswordAsync(native, "NativeAdmin!123")); Assert.Equal(Role.Admin, native.Role);
        Assert.False(await manager.CheckPasswordAsync(native, "password"));
        Assert.NotNull(await manager.FindByNameAsync(other.UserName));
        var retained = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("GZCTF.PlatformImport.History.v1").Unprotect(skippedMember.ProtectedMetadata);
        Assert.DoesNotContain(LegacyHash, retained);
        // A later explicit skip also prevents linking an account imported by an earlier batch.
        var laterOptions = options with { SkipUserIds = [skipped.SourceId, other.SourceId] };
        var laterBatch = Batch(laterOptions); laterBatch.PlanFingerprint = new string('c', 64);
        db.PlatformImportBatches.Add(laterBatch);
        Assert.Equal(0, await accounts.ImportAsync(package, laterOptions, laterBatch));
        await history.ImportAsync(package, laterOptions, laterBatch);
        Assert.All(await db.PlatformHistoryMembers.Where(m => m.BatchId == laterBatch.Id).ToListAsync(), m => Assert.Null(m.UserId));
    }

    [Fact]
    public async Task Explicit_skips_must_name_real_unique_source_users_and_do_not_hide_duplicate_ids()
    {
        using var scope = factory.Services.CreateScope();
        var accounts = new ImportedAccountService(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<UserManager<UserInfo>>());
        var source = User("duplicate", Name());
        var options = new PlatformImportOptions("skip-validation-" + Guid.NewGuid().ToString("N"), SkipUserIds: ["unknown", "duplicate"]);
        var issues = await accounts.PreviewAsync(Package([source, source]), options);
        Assert.Contains(issues, i => i.Code == "invalid_skip_user" && i.Severity == "error");
        Assert.Contains(issues, i => i.Code == "duplicate_source_user" && i.Severity == "error");
    }

    [Fact]
    public async Task Username_override_resolves_name_only_and_missing_email_stays_blocking()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var accounts = new ImportedAccountService(db, scope.ServiceProvider.GetRequiredService<UserManager<UserInfo>>());
        var source = User("1", "this-name-is-far-too-long");
        var options = new PlatformImportOptions("override-" + Guid.NewGuid().ToString("N"), UserNameOverrides: new Dictionary<string, string> { { "1", Name() } });
        Assert.DoesNotContain(await accounts.PreviewAsync(Package([source]), options), i => i.Code == "invalid_user_name");
        Assert.Contains(await accounts.PreviewAsync(Package([source with { Email = null }]), options), i => i.Code == "invalid_user_email" && i.Severity == "error");
    }

    [Fact]
    public async Task History_uses_imported_membership_snapshot_and_never_exposes_flags_or_grants_training_credit()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<UserInfo>>();
        var accounts = new ImportedAccountService(db, manager);
        var history = new PlatformHistoryService(db, scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
        var first = User("1", Name()) with { TeamId = "A" };
        var second = User("2", Name()) with { TeamId = "B" };
        var third = User("3", Name()) with { TeamId = "missing" };
        const string secret = "{\"provided\":\"flag{private-source}\",\"plugin\":\"unsupported\"}";
        var package = Package([first, second, third]) with
        {
            Teams = [new("A", "Team Alpha", "{}"), new("B", "Team Beta", "{}")],
            Submissions = [new("personal", "1", null, "challenge", "correct", DateTimeOffset.UtcNow, 100, secret),
                new("team-a", null, "A", "challenge", "correct", DateTimeOffset.UtcNow, 100, secret),
                new("team-b", null, "B", "challenge", "incorrect", DateTimeOffset.UtcNow, null, secret)],
            Awards = [new("award-a", null, "A", "First place", 1000, DateTimeOffset.UtcNow, secret)]
        };
        var options = new PlatformImportOptions("history-" + Guid.NewGuid().ToString("N"));
        var batch = Batch(options);
        db.PlatformImportBatches.Add(batch);
        await accounts.ImportAsync(package, options, batch);
        await history.ImportAsync(package, options, batch);
        var localFirst = (await manager.FindByNameAsync(first.UserName))!;
        var page = await history.GetAsync(localFirst.Id, false);
        Assert.Equal(2, page.SubmissionCount);
        Assert.Single(page.Awards);
        Assert.Contains(page.Submissions, s => s.SourceId == "personal" && s.Attribution == "personal");
        Assert.Contains(page.Submissions, s => s.SourceId == "team-a" && s.Attribution == "team");
        Assert.DoesNotContain(page.Submissions, s => s.SourceId == "team-b");
        var localThird = (await manager.FindByNameAsync(third.UserName))!;
        var thirdPage = await history.GetAsync(localThird.Id, false);
        Assert.Empty(thirdPage.Submissions);
        Assert.Single(thirdPage.Members!);
        Assert.Equal(third.UserName, thirdPage.Members![0].SourceUserName);
        Assert.Equal(1, thirdPage.MemberCount);
        Assert.Contains(thirdPage.Batches, b => b.Id == batch.Id);
        Assert.Empty((await history.GetAsync(Guid.NewGuid(), false)).Submissions);
        var adminHistory = await history.GetAsync(localThird.Id, true);
        Assert.Equal(3, adminHistory.Submissions.Count(s => s.BatchId == batch.Id));
        var json = JsonSerializer.Serialize(page);
        Assert.DoesNotContain("flag{", json);
        Assert.DoesNotContain("PasswordHash", json);
        Assert.DoesNotContain("Metadata", json);
        Assert.False(await db.ChallengeProgress.AnyAsync(p => p.UserId == localFirst.Id));
        var stored = await db.PlatformHistorySubmissions.FirstAsync(s => s.BatchId == batch.Id);
        Assert.DoesNotContain("flag{", stored.ProtectedMetadata);
        Assert.Contains("flag{private-source}", scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("GZCTF.PlatformImport.History.v1").Unprotect(stored.ProtectedMetadata));
    }

    [Fact]
    public async Task Roster_without_submissions_is_visible_only_to_its_owner_or_an_administrator()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<UserInfo>>();
        var history = new PlatformHistoryService(db, scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
        var first = User("first", Name()) with { TeamId = "team" };
        var second = User("second", Name());
        var package = Package([first, second]) with { Teams = [new("team", "Imported team", "{}")] };
        var options = new PlatformImportOptions("roster-" + Guid.NewGuid().ToString("N"));
        var batch = Batch(options);
        db.PlatformImportBatches.Add(batch);
        await new ImportedAccountService(db, manager).ImportAsync(package, options, batch);
        await history.ImportAsync(package, options, batch);
        var owner = (await manager.FindByNameAsync(first.UserName))!;
        var ownPage = await history.GetAsync(owner.Id, false);
        Assert.Empty(ownPage.Submissions);
        Assert.Empty(ownPage.Awards);
        var ownMember = Assert.Single(ownPage.Members!);
        Assert.Equal(first.UserName, ownMember.SourceUserName);
        Assert.Equal("Imported team", ownMember.SourceTeamName);
        Assert.Equal(1, ownPage.MemberCount);
        Assert.Single(ownPage.Batches);
        Assert.Empty((await history.GetAsync(Guid.NewGuid(), false)).Members!);
        var adminPage = await history.GetAsync(Guid.NewGuid(), true);
        Assert.Contains(adminPage.Members!, m => m.BatchId == batch.Id && m.SourceUserId == "first");
        Assert.Contains(adminPage.Members!, m => m.BatchId == batch.Id && m.SourceUserId == "second");
        var json = JsonSerializer.Serialize(ownMember);
        Assert.DoesNotContain(first.Email!, json);
        Assert.DoesNotContain("Password", json);
        Assert.DoesNotContain("Metadata", json);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Authentication_material_is_removed_before_private_history_retention(bool includeHistory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var protection = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var protector = protection.CreateProtector("GZCTF.PlatformImport.History.v1");
        var history = new PlatformHistoryService(db, protection);
        const string raw = """
            {"affiliation":"Retained public school","plugin":{"difficulty":"hard","secret":"AUTH-MATERIAL-1"},"sessions":[{"token":"AUTH-MATERIAL-2"}],"users":[{"password":"AUTH-MATERIAL-3"}],"config":[{"key":"smtp_password","value":"AUTH-MATERIAL-4"}]}
            """;
        var source = User("source", Name()) with { MetadataJson = raw, PasswordHash = "AUTH-MATERIAL-HASH" };
        var package = Package([source]) with
        {
            MetadataJson = raw,
            Teams = [new("team", "Safe team", raw)],
            Submissions = [new("submit", "source", null, "challenge", "discard", DateTimeOffset.UtcNow, null, raw)],
            Awards = [new("award", "source", null, "Safe award", 1, DateTimeOffset.UtcNow, raw)]
        };
        var options = new PlatformImportOptions("sanitized-" + Guid.NewGuid().ToString("N"), IncludeHistory: includeHistory);
        var batch = Batch(options);
        db.PlatformImportBatches.Add(batch);
        await history.ImportAsync(package, options, batch);
        await db.SaveChangesAsync();
        var retained = protector.Unprotect(batch.ProtectedMetadata!);
        Assert.DoesNotContain("AUTH-MATERIAL", retained);
        if (includeHistory)
        {
            var member = await db.PlatformHistoryMembers.SingleAsync(m => m.BatchId == batch.Id);
            var submit = await db.PlatformHistorySubmissions.SingleAsync(m => m.BatchId == batch.Id);
            var award = await db.PlatformHistoryAwards.SingleAsync(m => m.BatchId == batch.Id);
            var retainedAccount = protector.Unprotect(member.ProtectedMetadata);
            Assert.DoesNotContain("AUTH-MATERIAL", retainedAccount);
            Assert.Contains("Retained public school", retainedAccount);
            Assert.DoesNotContain("AUTH-MATERIAL", protector.Unprotect(submit.ProtectedMetadata));
            Assert.DoesNotContain("AUTH-MATERIAL", protector.Unprotect(award.ProtectedMetadata));
            Assert.Equal("discard", submit.Result);
        }
        else
        {
            Assert.False(await db.PlatformHistoryMembers.AnyAsync(m => m.BatchId == batch.Id));
            Assert.False(await db.PlatformHistorySubmissions.AnyAsync(m => m.BatchId == batch.Id));
        }
    }

    [Fact]
    public async Task Ambient_transaction_rollback_removes_account_mapping_and_batch()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<UserInfo>>();
        var source = User("1", Name());
        var options = new PlatformImportOptions("rollback-" + Guid.NewGuid().ToString("N"));
        var batch = Batch(options);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.PlatformImportBatches.Add(batch);
            Assert.Equal(1, await new ImportedAccountService(db, manager).ImportAsync(Package([source]), options, batch));
            await transaction.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        Assert.False(await db.Users.AnyAsync(u => u.UserName == source.UserName));
        Assert.False(await db.PlatformImportMaps.AnyAsync(m => m.SourceInstance == options.SourceInstance));
        Assert.False(await db.PlatformImportBatches.AnyAsync(b => b.Id == batch.Id));
    }

    private static string Name() => "imp" + Guid.NewGuid().ToString("N")[..10];
    private static PlatformImportUser User(string id, string name) => new(id, name, Name() + "@example.test", LegacyHash, true, false, null, DateTimeOffset.UtcNow, "{}");
    private static PlatformImportBatch Batch(PlatformImportOptions options) => new() { SourceInstance = options.SourceInstance, Format = "ctfd", Fingerprint = new string('a', 64), PlanFingerprint = new string('b', 64), Title = "Imported event" };
    private static PlatformImportPackage Package(IReadOnlyList<PlatformImportUser> users) => new("ctfd", new string('a', 64), "Imported event", [], users, [], [], [], new Dictionary<string, byte[]>(), [], "{}");
}
