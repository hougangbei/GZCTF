using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.Imports.Domain;
using GZCTF.Features.ChallengeRuntime.Application;
using GZCTF.Features.Leaderboard.Application;
using GZCTF.Features.LearningProgress.Domain;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Imports;
[Collection(nameof(IntegrationTestCollection))]
public sealed class ScoreBaselineLeaderboardTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Fixed_baseline_survives_unpublished_questions_and_native_solves_add_once()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Score!12345");
        var imported = new Challenge { Type = ChallengeType.StaticAttachment, Score = 9999, IsEnabled = false, PublicationState = ChallengePublicationState.Draft, Flags = [new ChallengeFlag { Kind = ChallengeFlagKind.Static, Value = "fixture-old" }] };
        var native = PublishedQuestion(db);
        var baseline = Baseline(db, user.Id, 250, 2);
        db.ChallengeProgress.Add(new ChallengeProgress { UserId = user.Id, Challenge = imported, ImportedScoreBaseline = baseline, SolvedAtUtc = DateTimeOffset.Parse("2020-01-01T00:00:00Z") });
        db.ChallengeProgress.Add(new ChallengeProgress { UserId = user.Id, Challenge = native, SolvedAtUtc = baseline.CreatedAtUtc.AddDays(1) });
        await db.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new LearningLeaderboardService(db, cache);
        var board = await service.GetAsync("score", default);
        var entry = Assert.Single(board.Entries, e => e.UserName == user.UserName);
        Assert.Equal(350, entry.Score);
        Assert.Equal(3, entry.SolvedCount);
        Assert.Equal(250, entry.ImportedScore);
        Assert.Equal(100, entry.EarnedScore);
        Assert.Equal(2, entry.ImportedSolvedCount);
        Assert.Equal(1, entry.EarnedSolvedCount);
        Assert.True(board.HasImportedScores);
        var points = Assert.Single(board.Series, s => s.UserName == user.UserName).Points;
        Assert.Equal(250, points[0].Score);
        Assert.Equal(DateOnly.FromDateTime(baseline.CreatedAtUtc.ToOffset(TimeSpan.FromHours(8)).Date), points[0].Date);
        Assert.Equal(350, points[^1].Score);
        imported.Score = 5;
        imported.IsEnabled = true;
        imported.PublicationState = ChallengePublicationState.Published;
        await db.SaveChangesAsync();
        cache.Remove("learning-leaderboard:score");
        Assert.Equal(350, Assert.Single((await service.GetAsync("score", default)).Entries, e => e.UserName == user.UserName).Score);
        Assert.Equal(350, Assert.Single((await service.GetAsync("solves", default)).Entries, e => e.UserName == user.UserName).Score);
        var submissions = scope.ServiceProvider.GetRequiredService<ChallengeSubmissionService>();
        var repeated = await submissions.SubmitAsync(user.Id, imported.Id, "fixture-old");
        Assert.True(repeated.Accepted);
        Assert.False(repeated.FirstSolve);
        var fresh = PublishedQuestion(db); fresh.Score = 75;
        fresh.Flags.Add(new ChallengeFlag { Kind = ChallengeFlagKind.Static, Value = "fixture-new" });
        await db.SaveChangesAsync();
        var first = await submissions.SubmitAsync(user.Id, fresh.Id, "fixture-new");
        Assert.True(first.Accepted); Assert.True(first.FirstSolve);
        cache.Remove("learning-leaderboard:score");
        var continued = Assert.Single((await service.GetAsync("score", default)).Entries, e => e.UserName == user.UserName);
        Assert.Equal(425, continued.Score); Assert.Equal(250, continued.ImportedScore); Assert.Equal(175, continued.EarnedScore);
        Assert.Equal(4, continued.SolvedCount);
    }

    [Fact]
    public async Task Hidden_and_banned_accounts_stay_out_even_with_native_points_and_inactive_accounts_do_not_rank()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hidden = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Score!12345");
        var banned = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Score!12345", role: Role.Banned);
        var idle = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Score!12345");
        var negative = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Score!12345");
        Baseline(db, hidden.Id, 10000, 20).Hidden = true;
        Baseline(db, banned.Id, 10000, 20);
        Baseline(db, idle.Id, 0, 0).HasActivity = false;
        Baseline(db, negative.Id, -50, 0);
        var native = PublishedQuestion(db);
        db.ChallengeProgress.AddRange(new ChallengeProgress { UserId = hidden.Id, Challenge = native }, new ChallengeProgress { UserId = banned.Id, Challenge = native });
        await db.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var board = await new LearningLeaderboardService(db, cache).GetAsync("score", default);
        Assert.DoesNotContain(board.Entries, e => e.UserName == hidden.UserName || e.UserName == banned.UserName || e.UserName == idle.UserName);
        Assert.Equal(-50, Assert.Single(board.Entries, e => e.UserName == negative.UserName).Score);
    }

    [Fact]
    public async Task Combined_score_uses_64bit_arithmetic()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Score!12345");
        Baseline(db, user.Id, int.MaxValue, 10);
        db.ChallengeProgress.Add(new ChallengeProgress { UserId = user.Id, Challenge = PublishedQuestion(db) });
        await db.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var board = await new LearningLeaderboardService(db, cache).GetAsync("score", default);
        Assert.Equal((long)int.MaxValue + 100, Assert.Single(board.Entries, e => e.UserName == user.UserName).Score);
    }

    private static PlatformScoreBaseline Baseline(AppDbContext db, Guid userId, int score, int solves)
    {
        var source = "board-" + Guid.NewGuid().ToString("N");
        var batch = new PlatformImportBatch { SourceInstance = source, Format = "CTFd", Fingerprint = new string('a', 64), PlanFingerprint = new string('b', 64), Title = "Synthetic" };
        db.PlatformImportBatches.Add(batch);
        var baseline = new PlatformScoreBaseline { UserId = userId, BatchId = batch.Id, SourceInstance = source, SourceUserId = "1", Score = score, SolvedCount = solves, HasActivity = true, CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-2), LastScoringAtUtc = DateTimeOffset.Parse("2020-01-01T00:00:00Z") };
        db.PlatformScoreBaselines.Add(baseline);
        return baseline;
    }
    private static Challenge PublishedQuestion(AppDbContext db)
    {
        var question = new Challenge { Type = ChallengeType.StaticAttachment, Score = 100, IsEnabled = true, PublicationState = ChallengePublicationState.Published };
        var tree = new SkillTree { Name = "Synthetic score board" };
        var revision = new SkillTreeRevision { Status = SkillTreeRevisionStatus.Published, Categories = [new SkillTreeCategoryRef { Category = new SkillCategory { Name = "Web", Skills = [new CategorySkillRef { Skill = new Skill { Name = "Web", Contents = [new SkillContent { Challenge = question }] } }] } }] };
        tree.Revisions.Add(revision); db.SkillTrees.Add(tree);
        // The current revision FK is assigned after EF has inserted both sides.
        db.SaveChanges(); tree.CurrentPublishedRevisionId = revision.Id;
        return question;
    }
}
