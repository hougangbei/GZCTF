using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.ChallengeLibrary.Application;
using GZCTF.Features.Imports.Application;
using GZCTF.Features.Imports.Domain;
using GZCTF.Features.LearningProgress.Domain;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Imports;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ScoreBaselineTests(GZCTFApplicationFactory factory)
{
    private static readonly DateTimeOffset At = new(2024, 1, 2, 0, 0, 0, TimeSpan.Zero);
    private static PlatformImportPackage Package(string fingerprint = "archive") => new("CTFd", fingerprint, "Snapshot",
        [new("q", "q", "", "Web", ChallengeType.StaticAttachment, "normal", 300, false, 0, [], [], [], null, "{}"),
         new("z", "z", "", "Web", ChallengeType.StaticAttachment, "normal", 0, false, 0, [], [], [], null, "{}")],
        [new("u", "test", "test@example.com", null, true, false, null, At, "{\"hidden\":true}"),
         new("skip", "skip", null, null, false, false, null, At, "{}")], [],
        [new("s1", "u", null, "q", "correct", At, null, "{}"), new("s2", "u", null, "q", "correct", At.AddDays(1), 999, "{}"),
         new("s3", "u", null, "z", "correct", At.AddDays(10), null, "{}")],
        [new("a", "u", null, "Penalty", -400, At.AddDays(2), "{}")], new Dictionary<string, byte[]>(), [], "{\"config\":[{\"key\":\"user_mode\",\"value\":\"users\"}]}");

    [Fact]
    public async Task Snapshot_is_unique_fixed_includes_negative_awards_and_restores_earliest_progress()
    {
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Snapshot!1234");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = "snapshot-" + Guid.NewGuid().ToString("N");
        var options = new PlatformImportOptions(source, SkipUserIds: ["skip"], MergeScores: true);
        var batch = new PlatformImportBatch { SourceInstance = source, Format = "CTFd", Fingerprint = "archive" };
        db.PlatformImportBatches.Add(batch);
        var q = new Challenge { Score = 999, Type = ChallengeType.StaticAttachment };
        var z = new Challenge { Score = 111, Type = ChallengeType.StaticAttachment };
        db.Challenges.AddRange(q, z);
        db.PlatformImportMaps.AddRange(new PlatformImportMap { SourceInstance = source, Kind = "user", SourceId = "u", TargetId = user.Id, Batch = batch },
            new PlatformImportMap { SourceInstance = source, Kind = "challenge", SourceId = "q", TargetId = q.Id, Batch = batch },
            new PlatformImportMap { SourceInstance = source, Kind = "challenge", SourceId = "z", TargetId = z.Id, Batch = batch });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<PlatformScoreMigrationService>();
        Assert.Empty(await service.PreviewAsync(Package(), options));
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await service.ImportAsync(Package(), options, batch);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        var baseline = await db.PlatformScoreBaselines.SingleAsync(b => b.UserId == user.Id);
        Assert.Equal(-100, baseline.Score);
        Assert.Equal(2, baseline.SolvedCount);
        Assert.True(baseline.Hidden);
        Assert.True(baseline.HasActivity);
        Assert.Equal(At.AddDays(2), baseline.LastScoringAtUtc);
        var progress = await db.ChallengeProgress.Where(p => p.UserId == user.Id).ToArrayAsync();
        Assert.Equal(2, progress.Length);
        Assert.Equal(At, progress.Single(p => p.ChallengeId == q.Id).SolvedAtUtc);
        Assert.All(progress, p => Assert.Equal(baseline.Id, p.ImportedScoreBaselineId));
        Assert.Equal(2, await db.LearnerDailySolveStats.Where(s => s.UserId == user.Id).SumAsync(s => s.SolveCount));
        await service.ImportAsync(Package(), options, batch);
        await db.SaveChangesAsync();
        Assert.Single(await db.PlatformScoreBaselines.Where(b => b.UserId == user.Id).ToArrayAsync());
        Assert.Equal(-100, baseline.Score);
        Assert.Contains(await service.PreviewAsync(Package("changed"), options), i => i.Code == "score_merge_source_changed");
        Assert.Contains(await service.PreviewAsync(Package(), options with { SkipUserIds = [] }), i => i.Code == "score_merge_source_changed");
    }

    [Fact]
    public async Task Invalid_modes_dates_options_and_native_progress_are_read_only_rejections()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PlatformScoreMigrationService>();
        var options = new PlatformImportOptions("preflight-" + Guid.NewGuid().ToString("N"), MergeScores: true);
        Assert.Contains(await service.PreviewAsync(Package(), options with { IncludeHistory = false }), i => i.Code == "score_merge_options_invalid");
        Assert.Contains(await service.PreviewAsync(Package() with { MetadataJson = "{\"config\":[{\"key\":\"user_mode\",\"value\":\"teams\"},{\"key\":\"freeze\",\"value\":123}]}" }, options), i => i.Code == "score_merge_requires_users_mode");
        Assert.Contains(await service.PreviewAsync(Package() with { MetadataJson = "{\"config\":[{\"key\":\"freeze\",\"value\":123}]}" }, options), i => i.Code == "score_merge_frozen_scoreboard");
        Assert.Contains(await service.PreviewAsync(Package() with { Submissions = [Package().Submissions[0] with { SubmittedAtUtc = null }] }, options), i => i.Code == "score_merge_missing_date");
        Assert.Contains(await service.PreviewAsync(Package() with { Awards = [Package().Awards[0] with { TeamId = "team" }] }, options), i => i.Code == "score_merge_team_history");
        Assert.Empty(db.ChangeTracker.Entries());
    }
    [Fact]
    public async Task Concurrent_same_source_imports_commit_one_baseline_and_one_progress_set()
    {
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Concurrent!1234");
        var name = TestDataSeeder.RandomName();
        var package = Package() with { Users = [Package().Users[0] with { UserName = name, Email = name + "@example.com" }] };
        var source = "concurrent-score-" + Guid.NewGuid().ToString("N");
        var options = new PlatformImportOptions(source, MergeScores: true);
        using var scope = factory.Services.CreateScope();
        var importer = scope.ServiceProvider.GetRequiredService<PlatformImportService>();
        var preview = await importer.PreviewAsync(package, options);
        Assert.True(preview.CanImport);
        async Task<PlatformImportBatchInfo> Import()
        {
            await using var operation = factory.Services.CreateAsyncScope();
            return await operation.ServiceProvider.GetRequiredService<PlatformImportService>()
                .ImportAsync(package, options, preview.PlanFingerprint, admin.Id);
        }
        var results = await Task.WhenAll(Import(), Import());
        Assert.Equal(results[0].Id, results[1].Id);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var baseline = Assert.Single(await db.PlatformScoreBaselines.Where(b => b.SourceInstance == source).ToArrayAsync());
        Assert.Equal(-100, baseline.Score);
        Assert.Equal(2, await db.ChallengeProgress.CountAsync(p => p.ImportedScoreBaselineId == baseline.Id));
        Assert.Equal(1, await db.PlatformScoreMigrations.CountAsync(m => m.SourceInstance == source));
        Assert.Equal(1, await db.PlatformImportBatches.CountAsync(b => b.SourceInstance == source));
    }

    [Fact]
    public async Task Importer_replays_original_batch_and_blocks_existing_native_solves()
    {
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Snapshot!Admin123");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PlatformImportService>();
        var source = "import-score-" + Guid.NewGuid().ToString("N");
        var name = TestDataSeeder.RandomName();
        var package = Package() with { Users = [Package().Users[0] with { UserName = name, Email = name + "@example.com" }] };
        var options = new PlatformImportOptions(source, MergeScores: true);
        var preview = await service.PreviewAsync(package, options);
        Assert.True(preview.CanImport, string.Join(",", preview.Issues.Select(i => i.Code)));
        var historyPreview = await service.PreviewAsync(package, options with { MergeScores = false });
        Assert.NotEqual(preview.PlanFingerprint, historyPreview.PlanFingerprint);
        var batch = await service.ImportAsync(package, options, preview.PlanFingerprint, admin.Id);
        Assert.Equal(batch.Id, (await service.ImportAsync(package, options, preview.PlanFingerprint, admin.Id)).Id);
        var renamed = options with { TreeName = "Changed display title" };
        var renamedPreview = await service.PreviewAsync(package, renamed);
        Assert.Equal(batch.Id, (await service.ImportAsync(package, renamed, renamedPreview.PlanFingerprint, admin.Id)).Id);
        Assert.Single(await db.PlatformScoreMigrations.Where(m => m.SourceInstance == source).ToArrayAsync());

        var historySource = source + "-native";
        var otherName = TestDataSeeder.RandomName();
        var historyPackage = package with { Users = [package.Users[0] with { UserName = otherName, Email = otherName + "@example.com" }] };
        var historyOptions = new PlatformImportOptions(historySource);
        var hp = await service.PreviewAsync(historyPackage, historyOptions);
        await service.ImportAsync(historyPackage, historyOptions, hp.PlanFingerprint, admin.Id);
        var userId = await db.PlatformImportMaps.Where(m => m.SourceInstance == historySource && m.Kind == "user").Select(m => m.TargetId).SingleAsync();
        var challengeId = await db.PlatformImportMaps.Where(m => m.SourceInstance == historySource && m.Kind == "challenge" && m.SourceId == "q").Select(m => m.TargetId).SingleAsync();
        db.ChallengeProgress.Add(new ChallengeProgress { UserId = userId, ChallengeId = challengeId, SolvedAtUtc = At });
        await db.SaveChangesAsync();
        var conflict = await service.PreviewAsync(historyPackage, historyOptions with { MergeScores = true });
        Assert.False(conflict.CanImport);
        Assert.Contains(conflict.Issues, i => i.Code == "score_merge_native_progress_conflict");
        await Assert.ThrowsAsync<PlatformImportRejectedException>(() => service.ImportAsync(historyPackage, historyOptions with { MergeScores = true }, conflict.PlanFingerprint, admin.Id));
        Assert.False(await db.PlatformScoreBaselines.AnyAsync(b => b.UserId == userId));
    }

    [Fact]
    public async Task Baseline_failure_rolls_back_accounts_history_mappings_and_progress_together()
    {
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Snapshot!Admin123");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PlatformImportService>();
        var suffix = Guid.NewGuid().ToString("N");
        var source = "rollback-" + suffix;
        var name = TestDataSeeder.RandomName();
        var package = Package() with { Users = [Package().Users[0] with { UserName = name, Email = name + "@example.com" }] };
        var options = new PlatformImportOptions(source, MergeScores: true);
        var preview = await service.PreviewAsync(package, options);
        Assert.True(preview.CanImport);
#pragma warning disable EF1002 // Both identifiers and source are generated GUID strings.
        await db.Database.ExecuteSqlRawAsync($$"""
            CREATE FUNCTION fail_score_{{suffix}}() RETURNS trigger AS $body$
            BEGIN
                IF NEW."SourceInstance" = '{{source}}' THEN RAISE EXCEPTION 'forced baseline failure'; END IF;
                RETURN NEW;
            END;
            $body$ LANGUAGE plpgsql;
            CREATE TRIGGER fail_score_{{suffix}} BEFORE INSERT ON "PlatformScoreBaselines"
                FOR EACH ROW EXECUTE FUNCTION fail_score_{{suffix}}();
            """);
        try
        {
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => service.ImportAsync(package, options, preview.PlanFingerprint, admin.Id));
            db.ChangeTracker.Clear();
            Assert.False(await db.Users.AnyAsync(u => u.UserName == name));
            Assert.False(await db.PlatformImportBatches.AnyAsync(b => b.SourceInstance == source));
            Assert.False(await db.PlatformImportMaps.AnyAsync(m => m.SourceInstance == source));
            Assert.False(await db.PlatformScoreMigrations.AnyAsync(m => m.SourceInstance == source));
            Assert.False(await db.PlatformScoreBaselines.AnyAsync(b => b.SourceInstance == source));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync($$"""
                DROP TRIGGER IF EXISTS fail_score_{{suffix}} ON "PlatformScoreBaselines";
                DROP FUNCTION IF EXISTS fail_score_{{suffix}}();
                """);
        }
#pragma warning restore EF1002
    }

    [Fact]
    public async Task History_only_import_then_challenge_merge_rejects_baseline_without_restoring_obsolete_progress()
    {
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Snapshot!Admin123");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var importer = scope.ServiceProvider.GetRequiredService<PlatformImportService>();
        var source = "merged-score-" + Guid.NewGuid().ToString("N");
        var name = TestDataSeeder.RandomName();
        var package = Package() with { Users = [Package().Users[0] with { UserName = name, Email = name + "@example.com" }] };
        var options = new PlatformImportOptions(source);
        var preview = await importer.PreviewAsync(package, options);
        Assert.True(preview.CanImport);
        await importer.ImportAsync(package, options, preview.PlanFingerprint, admin.Id);
        var userId = await db.PlatformImportMaps.Where(m => m.SourceInstance == source && m.Kind == "user").Select(m => m.TargetId).SingleAsync();
        var oldId = await db.PlatformImportMaps.Where(m => m.SourceInstance == source && m.Kind == "challenge" && m.SourceId == "q").Select(m => m.TargetId).SingleAsync();
        var survivor = new Challenge { Score = 700 };
        db.Challenges.Add(survivor);
        var native = new ChallengeProgress { UserId = userId, Challenge = survivor, SolvedAtUtc = At.AddYears(1) };
        db.ChallengeProgress.Add(native);
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<ChallengeLibraryService>().MergeChallengesAsync(oldId, survivor.Id, default);
        var before = await db.ChallengeProgress.AsNoTracking().SingleAsync(p => p.UserId == userId);
        var mergedOptions = options with { MergeScores = true };
        var rejected = await importer.PreviewAsync(package, mergedOptions);
        Assert.False(rejected.CanImport);
        Assert.Contains(rejected.Issues, i => i.Code == "score_merge_challenge_mapping_invalid" && i.SourceId == "q");
        await Assert.ThrowsAsync<PlatformImportRejectedException>(() => importer.ImportAsync(package, mergedOptions, rejected.PlanFingerprint, admin.Id));
        Assert.False(await db.PlatformScoreBaselines.AnyAsync(b => b.UserId == userId));
        Assert.False(await db.PlatformScoreMigrations.AnyAsync(m => m.SourceInstance == source));
        var after = await db.ChallengeProgress.AsNoTracking().SingleAsync(p => p.UserId == userId);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(survivor.Id, after.ChallengeId);
        Assert.Equal(before.SolvedAtUtc, after.SolvedAtUtc);
        Assert.Null(after.ImportedScoreBaselineId);
    }

    [Fact]
    public async Task Existing_progress_survives_database_upgrade_and_keeps_null_baseline()
    {
        using var scope = factory.Services.CreateScope();
        var existing = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var name = "score_upgrade_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(existing.Database.GetConnectionString());
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(existing.Database.GetConnectionString()) { Database = name, Pooling = false };
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261102112538_AddPlatformImports");
            var user = new GZCTF.Models.Data.UserInfo { UserName = "old-score-user" };
            var challenge = new Challenge { Type = ChallengeType.StaticAttachment, Score = 123 };
            db.Users.Add(user); db.Challenges.Add(challenge); await db.SaveChangesAsync();
            var progressId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ChallengeProgress\" (\"Id\", \"UserId\", \"ChallengeId\", \"SolvedAtUtc\", \"SolveMode\") VALUES ({progressId}, {user.Id}, {challenge.Id}, {At}, {0})");
            await migrator.MigrateAsync();
            var progress = await db.ChallengeProgress.SingleAsync();
            Assert.Equal(progressId, progress.Id);
            Assert.Null(progress.ImportedScoreBaselineId);
            Assert.Empty(await db.PlatformScoreBaselines.ToArrayAsync());
            Assert.Empty(await db.PlatformScoreMigrations.ToArrayAsync());
            await migrator.MigrateAsync();
            Assert.Equal(1, await db.ChallengeProgress.CountAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

}
