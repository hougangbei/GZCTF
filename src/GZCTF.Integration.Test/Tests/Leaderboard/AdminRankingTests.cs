using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.ChallengeRuntime.Application;
using GZCTF.Features.Imports.Domain;
using GZCTF.Features.LearningProgress.Domain;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Features.Leaderboard.Application;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Leaderboard;
[Collection(nameof(IntegrationTestCollection))]
public sealed class AdminRankingTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Adjust_target_retry_payload_binding_and_reversal_are_consistent()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AdminRankingService>();
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Score!12345");
        var before = await service.DetailAsync(user.Id, default);
        var command = new RankingAdjustmentCommand(Guid.NewGuid(), before.User.Version, "delta", 1200, "奖励");
        var after = await service.AdjustAsync(user.Id, command, user.Id, "管理员", default);
        Assert.Equal(1200, after.User.Score); Assert.Equal(2, after.User.Level);
        Assert.Equal(1200, (await service.AdjustAsync(user.Id, command, user.Id, "管理员", default)).User.Score);
        await Assert.ThrowsAsync<RankingConflictException>(() => service.AdjustAsync(user.Id, command with { Value = 1300 }, user.Id, "管理员", default));
        await Assert.ThrowsAsync<RankingConflictException>(() => service.AdjustAsync(user.Id, command with { OperationId = Guid.NewGuid() }, user.Id, "管理员", default));
        var target = await service.AdjustAsync(user.Id, new(Guid.NewGuid(), after.User.Version, "target", -100, "目标分"), user.Id, "管理员", default);
        Assert.Equal(-100, target.User.Score); Assert.Equal(1, target.User.Level);
        var original = target.Operations.Single(o => o.Kind == "target");
        var reversed = await service.ReverseAsync(original.Id, new(Guid.NewGuid(), target.User.Version, "撤销"), user.Id, "管理员", default);
        Assert.Equal(1200, reversed.User.Score);
        await Assert.ThrowsAsync<RankingConflictException>(() => service.ReverseAsync(original.Id, new(Guid.NewGuid(), reversed.User.Version, "再次撤销"), user.Id, "管理员", default));
    }

    [Fact]
    public void Levels_and_csv_are_bounded_and_safe()
    {
        Assert.Equal(1, RankingScoreService.Level(-500, RankingScoreService.DefaultThresholds));
        Assert.Equal(10, RankingScoreService.Level(50000, RankingScoreService.DefaultThresholds));
        Assert.Throws<RankingValidationException>(() => RankingScoreService.ValidateThresholds([0, 0]));
        Assert.Throws<RankingValidationException>(() => RankingScoreService.ValidateThresholds([-1, 100]));
        Assert.Equal("\"'=SUM(1,2)\"", AdminRankingService.CsvCell("=SUM(1,2)"));
        Assert.Equal("\"a\"\"b\"", AdminRankingService.CsvCell("a\"b"));
    }
    [Fact]
    public async Task Imported_revoke_flag_restore_and_manual_restore_keep_frozen_baseline_and_history()
    {
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service=scope.ServiceProvider.GetRequiredService<AdminRankingService>();
        var user=await TestDataSeeder.CreateUserAsync(factory.Services,TestDataSeeder.RandomName(),"Score!12345");
        var challenge=PublishedQuestion(db);challenge.Score=9000;challenge.Flags.Add(new ChallengeFlag {Kind=ChallengeFlagKind.Static,Value="frozen-flag"});
        var baseline=Baseline(db,user.Id,250,2);
        var originalAt=DateTimeOffset.UtcNow.AddDays(-100);
        db.ChallengeProgress.Add(new ChallengeProgress {UserId=user.Id,Challenge=challenge,ImportedScoreBaseline=baseline,ImportedFrozenScore=125,SolvedAtUtc=originalAt});
        await db.SaveChangesAsync();
        var before=await service.DetailAsync(user.Id,default);
        var revoked=await service.SolveAsync(user.Id,challenge.Id,false,new(Guid.NewGuid(),before.User.Version,"撤销旧题"),user.Id,"admin",default);
        Assert.Equal(125,revoked.User.Score);Assert.Equal(1,revoked.User.SolvedCount);
        Assert.False(await db.ChallengeProgress.AnyAsync(p=>p.UserId==user.Id));
        var again=await service.SolveAsync(user.Id,challenge.Id,false,new(Guid.NewGuid(),revoked.User.Version,"重复撤销"),user.Id,"admin",default);
        Assert.Equal(125,again.User.Score);
        var runtime=scope.ServiceProvider.GetRequiredService<ChallengeSubmissionService>();
        var first=await runtime.SubmitAsync(user.Id,challenge.Id,"frozen-flag");Assert.True(first.FirstSolve);
        var restored=await service.DetailAsync(user.Id,default);Assert.Equal(250,restored.User.Score);Assert.Equal(2,restored.User.SolvedCount);
        Assert.False((await runtime.SubmitAsync(user.Id,challenge.Id,"frozen-flag")).FirstSolve);
        Assert.Equal(250,(await service.DetailAsync(user.Id,default)).User.Score);
        var next=await service.SolveAsync(user.Id,challenge.Id,false,new(Guid.NewGuid(),restored.User.Version,"再次撤销"),user.Id,"admin",default);
        var marked=await service.SolveAsync(user.Id,challenge.Id,true,new(Guid.NewGuid(),next.User.Version,"人工恢复"),user.Id,"admin",default);
        Assert.Equal(250,marked.User.Score);Assert.Equal(2,marked.User.SolvedCount);
        Assert.Equal(250,(await db.PlatformScoreBaselines.AsNoTracking().SingleAsync(b=>b.Id==baseline.Id)).Score);
        Assert.Equal(125,(await db.ChallengeProgress.AsNoTracking().SingleAsync(p=>p.UserId==user.Id)).ImportedFrozenScore);
        Assert.Equal(1,await db.LearnerDailySolveStats.Where(p=>p.UserId==user.Id).SumAsync(p=>p.SolveCount));
        Assert.Equal(2,await db.ChallengeSubmissions.CountAsync(p=>p.UserId==user.Id));
        var board=await scope.ServiceProvider.GetRequiredService<LearningLeaderboardService>().GetAsync("score",default);
        Assert.Equal(250,Assert.Single(board.Entries,e=>e.UserName==user.UserName).Score);
        Assert.Equal(250,Assert.Single(board.Series,e=>e.UserName==user.UserName).Points[^1].Score);
    }

    [Fact]
    public async Task Native_revoke_removes_current_value_and_correct_flag_earns_current_value_with_conflict_detection()
    {
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service=scope.ServiceProvider.GetRequiredService<AdminRankingService>();
        var user=await TestDataSeeder.CreateUserAsync(factory.Services,TestDataSeeder.RandomName(),"Score!12345");
        var challenge=PublishedQuestion(db);challenge.Flags.Add(new ChallengeFlag {Kind=ChallengeFlagKind.Static,Value="native-flag"});await db.SaveChangesAsync();
        var empty=await service.DetailAsync(user.Id,default);
        var marked=await service.SolveAsync(user.Id,challenge.Id,true,new(Guid.NewGuid(),empty.User.Version,"人工补记"),user.Id,"admin",default);
        Assert.Equal(100,marked.User.Score);Assert.Equal(1,marked.User.SolvedCount);
        var revoked=await service.SolveAsync(user.Id,challenge.Id,false,new(Guid.NewGuid(),marked.User.Version,"撤销新题"),user.Id,"admin",default);
        Assert.Equal(0,revoked.User.Score);Assert.Equal(0,revoked.User.SolvedCount);
        Assert.Equal(-100,Assert.Single(revoked.Operations,o=>o.Kind=="revoke_solve").Delta);
        Assert.Equal(-1,Assert.Single(revoked.Operations,o=>o.Kind=="revoke_solve").SolvedDelta);
        challenge.Score=250;await db.SaveChangesAsync();
        Assert.True((await scope.ServiceProvider.GetRequiredService<ChallengeSubmissionService>().SubmitAsync(user.Id,challenge.Id,"native-flag")).FirstSolve);
        var restored=await service.DetailAsync(user.Id,default);Assert.Equal(250,restored.User.Score);Assert.Equal(1,restored.User.SolvedCount);
        Assert.Equal(250,Assert.Single(restored.Challenges,c=>c.ChallengeId==challenge.Id).Score);
        await Assert.ThrowsAsync<RankingConflictException>(()=>service.AdjustAsync(user.Id,new(Guid.NewGuid(),revoked.User.Version,"target",999,"过期"),user.Id,"admin",default));
        Assert.Equal(250,(await service.DetailAsync(user.Id,default)).User.Score);
    }

    [Fact]
    public async Task Concurrent_retries_apply_once_and_different_operation_with_stale_version_conflicts()
    {
        var user=await TestDataSeeder.CreateUserAsync(factory.Services,TestDataSeeder.RandomName(),"Score!12345");
        string version;
        using(var initial=factory.Services.CreateScope())version=(await initial.ServiceProvider.GetRequiredService<AdminRankingService>().DetailAsync(user.Id,default)).User.Version;
        var command=new RankingAdjustmentCommand(Guid.NewGuid(),version,"delta",42,"并发奖励");
        async Task<RankingDetailResponse> Apply(RankingAdjustmentCommand c)
        {
            using var scope=factory.Services.CreateScope();return await scope.ServiceProvider.GetRequiredService<AdminRankingService>().AdjustAsync(user.Id,c,user.Id,"admin",default);
        }
        var results=await Task.WhenAll(Apply(command),Apply(command));
        Assert.All(results,r=>Assert.Equal(42,r.User.Score));
        await Assert.ThrowsAsync<RankingConflictException>(()=>Apply(command with {OperationId=Guid.NewGuid()}));
        using var check=factory.Services.CreateScope();
        Assert.Equal(1,await check.ServiceProvider.GetRequiredService<AppDbContext>().RankingOperations.CountAsync(o=>o.UserId==user.Id));
    }

    [Fact]
    public async Task Hidden_override_applies_to_native_imported_rows_and_ban_still_dominates_public_chart()
    {
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service=scope.ServiceProvider.GetRequiredService<AdminRankingService>();
        var user=await TestDataSeeder.CreateUserAsync(factory.Services,TestDataSeeder.RandomName(),"Score!12345");
        Baseline(db,user.Id,100,1).Hidden=true;await db.SaveChangesAsync();
        var before=await service.DetailAsync(user.Id,default);Assert.True(before.User.Hidden);
        var visible=await service.VisibilityAsync(user.Id,new(Guid.NewGuid(),before.User.Version,false,"恢复显示"),user.Id,"admin",default);
        Assert.False(visible.User.Hidden);Assert.True(visible.User.SourceHidden);
        var board=scope.ServiceProvider.GetRequiredService<LearningLeaderboardService>();
        Assert.Contains((await board.GetAsync("score",default)).Entries,e=>e.UserName==user.UserName);
        var hidden=await service.VisibilityAsync(user.Id,new(Guid.NewGuid(),visible.User.Version,true,"隐藏"),user.Id,"admin",default);
        var concealed=await board.GetAsync("score",default);Assert.DoesNotContain(concealed.Entries,e=>e.UserName==user.UserName);Assert.DoesNotContain(concealed.Series,e=>e.UserName==user.UserName);
        await service.VisibilityAsync(user.Id,new(Guid.NewGuid(),hidden.User.Version,false,"恢复"),user.Id,"admin",default);
        await db.Users.Where(u=>u.Id==user.Id).ExecuteUpdateAsync(u=>u.SetProperty(v=>v.Role,Role.Banned));
        var banned=await board.GetAsync("score",default);Assert.DoesNotContain(banned.Entries,e=>e.UserName==user.UserName);Assert.DoesNotContain(banned.Series,e=>e.UserName==user.UserName);
        var page=await service.ListAsync(user.UserName,0,10,null,true,default);Assert.Single(page.Items);
    }

    [Fact]
    public async Task Invalid_adjustment_rolls_back_journal_state_and_score()
    {
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service=scope.ServiceProvider.GetRequiredService<AdminRankingService>();
        var user=await TestDataSeeder.CreateUserAsync(factory.Services,TestDataSeeder.RandomName(),"Score!12345");
        Baseline(db,user.Id,int.MaxValue,1);await db.SaveChangesAsync();var before=await service.DetailAsync(user.Id,default);
        await Assert.ThrowsAsync<RankingValidationException>(()=>service.AdjustAsync(user.Id,new(Guid.NewGuid(),before.User.Version,"delta",1,"溢出"),user.Id,"admin",default));
        var after=await service.DetailAsync(user.Id,default);Assert.Equal(before.User.Score,after.User.Score);Assert.Equal(before.User.Version,after.User.Version);
        Assert.Empty(await db.RankingOperations.Where(o=>o.UserId==user.Id).ToArrayAsync());
        Assert.False(await db.RankingUserStates.AnyAsync(o=>o.UserId==user.Id));
    }

    [Fact]
    public async Task Levels_preview_change_and_retry_recompute_scores_without_changing_points()
    {
        using var scope=factory.Services.CreateScope();var service=scope.ServiceProvider.GetRequiredService<AdminRankingService>();
        var scores=scope.ServiceProvider.GetRequiredService<RankingScoreService>();
        var user=await TestDataSeeder.CreateUserAsync(factory.Services,TestDataSeeder.RandomName(),"Score!12345");
        var empty=await service.DetailAsync(user.Id,default);
        await service.AdjustAsync(user.Id,new(Guid.NewGuid(),empty.User.Version,"delta",500,"等级测试"),user.Id,"admin",default);
        var initial=await scores.LevelsAsync(default);
        try
        {
            Assert.True(await service.PreviewLevelsAsync(new(initial.Version,[0,100]),default)>0);
            var command=new RankingLevelsCommand(Guid.NewGuid(),initial.Version,[0,100]);
            var changed=await service.SetLevelsAsync(command,user.Id,"admin",default);
            Assert.Equal(changed.Version,(await service.SetLevelsAsync(command,user.Id,"admin",default)).Version);
            var detail=await service.DetailAsync(user.Id,default);Assert.Equal(500,detail.User.Score);Assert.Equal(2,detail.User.Level);
            await Assert.ThrowsAsync<RankingConflictException>(()=>service.SetLevelsAsync(command with {OperationId=Guid.NewGuid()},user.Id,"admin",default));
        }
        finally {var current=await scores.LevelsAsync(default);await service.SetLevelsAsync(new(Guid.NewGuid(),current.Version,initial.Thresholds),user.Id,"admin",default);}
    }

    [Fact]
    public async Task Api_permissions_audit_conflict_and_safe_export_have_no_private_data()
    {
        var name=TestDataSeeder.RandomName();var user=await TestDataSeeder.CreateUserAsync(factory.Services,name,"Score!12345");
        using var ordinary=factory.CreateClient();(await ordinary.PostAsJsonAsync("/api/Account/LogIn",new LoginModel {UserName=name,Password="Score!12345"})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden,(await ordinary.GetAsync("/api/admin/learning-rankings")).StatusCode);
        var adminName=TestDataSeeder.RandomName();await TestDataSeeder.CreateUserAsync(factory.Services,adminName,"Score!12345",role:Role.Admin);
        using var admin=factory.CreateClient();(await admin.PostAsJsonAsync("/api/Account/LogIn",new LoginModel {UserName=adminName,Password="Score!12345"})).EnsureSuccessStatusCode();
        using(var scope=factory.Services.CreateScope())await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u=>u.Id==user.Id).ExecuteUpdateAsync(u=>u.SetProperty(v=>v.Bio,"=SUM(1,2)"));
        var detail=(await admin.GetFromJsonAsync<RankingDetailResponse>($"/api/admin/learning-rankings/{user.Id}"))!;
        var command=new RankingAdjustmentCommand(Guid.NewGuid(),detail.User.Version,"delta",77,"API奖励");
        (await admin.PostAsJsonAsync($"/api/admin/learning-rankings/{user.Id}/adjustments",command)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await admin.PostAsJsonAsync($"/api/admin/learning-rankings/{user.Id}/adjustments",command with {OperationId=Guid.NewGuid()})).StatusCode);
        var csv=await admin.GetStringAsync($"/api/admin/learning-rankings/export?search={name}");
        Assert.Contains("'=SUM(1,2)",csv);Assert.DoesNotContain(user.Email!,csv);Assert.DoesNotContain("password",csv,StringComparison.OrdinalIgnoreCase);
        using var check=factory.Services.CreateScope();Assert.True(await check.ServiceProvider.GetRequiredService<AppDbContext>().AuditEvents.AnyAsync(e=>e.Action=="learning_rankings.adjust"&&e.Succeeded));
    }


    [Fact]
    public async Task Visibility_only_idle_accounts_do_not_rank_or_change_scoring_tie_timestamp()
    {
        using var scope=factory.Services.CreateScope();var service=scope.ServiceProvider.GetRequiredService<AdminRankingService>();
        var scores=scope.ServiceProvider.GetRequiredService<RankingScoreService>();
        var user=await TestDataSeeder.CreateUserAsync(factory.Services,TestDataSeeder.RandomName(),"Score!12345");
        var before=(await scores.SnapshotAsync(default)).Single(s=>s.User.UserId==user.Id);
        var hidden=await service.VisibilityAsync(user.Id,new(Guid.NewGuid(),before.User.Version,true,"隐藏空账号"),user.Id,"admin",default);
        await service.VisibilityAsync(user.Id,new(Guid.NewGuid(),hidden.User.Version,false,"恢复空账号"),user.Id,"admin",default);
        var after=(await scores.SnapshotAsync(default)).Single(s=>s.User.UserId==user.Id);
        Assert.False(after.HasActivity);Assert.Equal(before.LastScoringAtUtc,after.LastScoringAtUtc);
    }

    [Fact]
    public async Task Merge_moves_disjoint_frozen_correction_to_survivor_and_old_id_cannot_reaward()
    {
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service=scope.ServiceProvider.GetRequiredService<AdminRankingService>();
        var user=await TestDataSeeder.CreateUserAsync(factory.Services,TestDataSeeder.RandomName(),"Score!12345");
        var duplicate=PublishedQuestion(db);duplicate.Flags.Add(new ChallengeFlag {Kind=ChallengeFlagKind.Static,Value="duplicate"});
        var survivor=PublishedQuestion(db);survivor.Score=999;survivor.Flags.Add(new ChallengeFlag {Kind=ChallengeFlagKind.Static,Value="survivor"});
        var baseline=Baseline(db,user.Id,125,1);
        db.ChallengeProgress.Add(new ChallengeProgress {UserId=user.Id,Challenge=duplicate,ImportedScoreBaseline=baseline,ImportedFrozenScore=125});await db.SaveChangesAsync();
        var before=await service.DetailAsync(user.Id,default);
        await service.SolveAsync(user.Id,duplicate.Id,false,new(Guid.NewGuid(),before.User.Version,"合并前撤销"),user.Id,"admin",default);
        await scope.ServiceProvider.GetRequiredService<GZCTF.Features.ChallengeLibrary.Application.ChallengeLibraryService>().MergeChallengesAsync(duplicate.Id,survivor.Id,default);
        Assert.False(await db.RankingSolveCorrections.AnyAsync(c=>c.UserId==user.Id&&c.ChallengeId==duplicate.Id));
        Assert.True(await db.RankingSolveCorrections.AnyAsync(c=>c.UserId==user.Id&&c.ChallengeId==survivor.Id&&c.Revoked));
        var runtime=scope.ServiceProvider.GetRequiredService<ChallengeSubmissionService>();
        Assert.False((await runtime.SubmitAsync(user.Id,duplicate.Id,"duplicate")).Accepted);
        Assert.True((await runtime.SubmitAsync(user.Id,survivor.Id,"survivor")).FirstSolve);
        Assert.Equal(125,(await service.DetailAsync(user.Id,default)).User.Score);
        Assert.False((await runtime.SubmitAsync(user.Id,survivor.Id,"survivor")).FirstSolve);
    }

    [Fact]
    public async Task Imported_existing_rows_resolve_source_value_when_frozen_column_is_null()
    {
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service=scope.ServiceProvider.GetRequiredService<AdminRankingService>();
        var user=await TestDataSeeder.CreateUserAsync(factory.Services,TestDataSeeder.RandomName(),"Score!12345");
        var challenge=PublishedQuestion(db);challenge.Score=999;
        var baseline=Baseline(db,user.Id,125,1);
        var batch=db.PlatformImportBatches.Local.Single(b=>b.Id==baseline.BatchId);
        var protector=scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>().CreateProtector("GZCTF.PlatformImport.History.v1");
        batch.ProtectedMetadata=protector.Protect("{\"Source\":{\"challenges\":[{\"id\":\"old1\",\"value\":125}]}}");
        db.PlatformImportMaps.Add(new PlatformImportMap {SourceInstance=baseline.SourceInstance,Kind="challenge",SourceId="old1",TargetId=challenge.Id,BatchId=batch.Id});
        db.ChallengeProgress.Add(new ChallengeProgress {UserId=user.Id,Challenge=challenge,ImportedScoreBaseline=baseline});await db.SaveChangesAsync();
        var before=await service.DetailAsync(user.Id,default);
        Assert.Equal(125,Assert.Single(before.Challenges,c=>c.ChallengeId==challenge.Id).Score);
        Assert.True(Assert.Single(before.Challenges,c=>c.ChallengeId==challenge.Id).FrozenScoreAvailable);
        var revoked=await service.SolveAsync(user.Id,challenge.Id,false,new(Guid.NewGuid(),before.User.Version,"迁移后撤销"),user.Id,"admin",default);
        Assert.Equal(0,revoked.User.Score);Assert.Equal(125,(await db.RankingSolveCorrections.SingleAsync(c=>c.UserId==user.Id)).FrozenScore);
    }


    [Fact]
    public async Task Existing_baseline_and_progress_survive_ranking_management_database_upgrade()
    {
        using var scope=factory.Services.CreateScope();var existing=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var databaseName="ranking_upgrade_"+Guid.NewGuid().ToString("N");
        await using var admin=new Npgsql.NpgsqlConnection(existing.Database.GetConnectionString());await admin.OpenAsync();
        await using(var create=new Npgsql.NpgsqlCommand($"CREATE DATABASE {databaseName}",admin))await create.ExecuteNonQueryAsync();
        try
        {
            var connection=new Npgsql.NpgsqlConnectionStringBuilder(existing.Database.GetConnectionString()) {Database=databaseName,Pooling=false};
            await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options);
            var migrator=Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);
            await migrator.MigrateAsync("20261104123652_AddPlatformScoreBaselines");
            var user=new GZCTF.Models.Data.UserInfo {UserName="before-ranking"};db.Users.Add(user);
            var challenge=new Challenge {Type=ChallengeType.StaticAttachment,Score=999};db.Challenges.Add(challenge);await db.SaveChangesAsync();
            var baseline=Baseline(db,user.Id,125,1);await db.SaveChangesAsync();
            var id=Guid.NewGuid();var at=DateTimeOffset.UtcNow.AddDays(-10);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ChallengeProgress\" (\"Id\", \"UserId\", \"ChallengeId\", \"SolvedAtUtc\", \"SolveMode\", \"ImportedScoreBaselineId\") VALUES ({id},{user.Id},{challenge.Id},{at},{0},{baseline.Id})");
            await migrator.MigrateAsync();db.ChangeTracker.Clear();
            var progress=await db.ChallengeProgress.SingleAsync();Assert.Equal(id,progress.Id);Assert.Equal(baseline.Id,progress.ImportedScoreBaselineId);Assert.Null(progress.ImportedFrozenScore);
            Assert.Equal(125,(await db.PlatformScoreBaselines.SingleAsync()).Score);Assert.Empty(await db.RankingOperations.ToArrayAsync());
            Assert.Equal(10,(await new RankingScoreService(db).LevelsAsync(default)).Thresholds.Length);
            await migrator.MigrateAsync();Assert.Equal(1,await db.ChallengeProgress.CountAsync());
        }
        finally {await using var drop=new Npgsql.NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)",admin);await drop.ExecuteNonQueryAsync();}
    }


    [Fact]
    public async Task Missing_import_source_value_blocks_revoke_and_preserves_progress_and_baseline()
    {
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service=scope.ServiceProvider.GetRequiredService<AdminRankingService>();
        var user=await TestDataSeeder.CreateUserAsync(factory.Services,TestDataSeeder.RandomName(),"Score!12345");
        var challenge=PublishedQuestion(db);challenge.Score=999;
        var baseline=Baseline(db,user.Id,125,1);
        db.ChallengeProgress.Add(new ChallengeProgress {UserId=user.Id,Challenge=challenge,ImportedScoreBaseline=baseline});await db.SaveChangesAsync();
        var before=await service.DetailAsync(user.Id,default);
        Assert.False(Assert.Single(before.Challenges,c=>c.ChallengeId==challenge.Id).FrozenScoreAvailable);
        var error=await Assert.ThrowsAsync<RankingValidationException>(()=>service.SolveAsync(user.Id,challenge.Id,false,new(Guid.NewGuid(),before.User.Version,"不能猜测旧分"),user.Id,"admin",default));
        Assert.Equal("ranking.import_frozen_score_unavailable",error.Message);
        Assert.True(await db.ChallengeProgress.AnyAsync(p=>p.UserId==user.Id));
        Assert.Equal(125,(await service.DetailAsync(user.Id,default)).User.Score);
        Assert.False(await db.RankingOperations.AnyAsync(o=>o.UserId==user.Id));
    }


    [Fact]
    public async Task Merge_waiting_for_solve_lock_reads_progress_committed_before_it_acquires_lock()
    {
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user=await TestDataSeeder.CreateUserAsync(factory.Services,TestDataSeeder.RandomName(),"Score!12345");
        var duplicate=PublishedQuestion(db);var survivor=PublishedQuestion(db);await db.SaveChangesAsync();
        await using var transaction=await db.Database.BeginTransactionAsync();
        await RankingScoreService.LockAsync(db,user.Id,default);
        var started=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Merge()
        {
            using var merging=factory.Services.CreateScope();var context=merging.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Database.OpenConnectionAsync();started.SetResult(((Npgsql.NpgsqlConnection)context.Database.GetDbConnection()).ProcessID);
            await merging.ServiceProvider.GetRequiredService<GZCTF.Features.ChallengeLibrary.Application.ChallengeLibraryService>().MergeChallengesAsync(duplicate.Id,survivor.Id,default);
        }
        var merge=Merge();var pid=await started.Task;
        await using var observer=new Npgsql.NpgsqlConnection(db.Database.GetConnectionString());await observer.OpenAsync();
        var waiting=false;
        for(var attempt=0;attempt<100&&!waiting;attempt++)
        {
            await using var check=new Npgsql.NpgsqlCommand("SELECT wait_event = 'advisory' FROM pg_stat_activity WHERE pid = @pid",observer);check.Parameters.AddWithValue("pid",pid);
            waiting=await check.ExecuteScalarAsync() is true;
            if(!waiting)await Task.Delay(10);
        }
        Assert.True(waiting,"Merge should wait for the solve/account lock before reading progress.");
        db.ChallengeProgress.Add(new ChallengeProgress {UserId=user.Id,ChallengeId=duplicate.Id});await db.SaveChangesAsync();
        await transaction.CommitAsync();await merge;
        var progress=await db.ChallengeProgress.AsNoTracking().SingleAsync(p=>p.UserId==user.Id);
        Assert.Equal(survivor.Id,progress.ChallengeId);
    }

    private static PlatformScoreBaseline Baseline(AppDbContext db,Guid userId,int score,int solves)
    {
        var source="admin-board-"+Guid.NewGuid().ToString("N");
        var batch=new PlatformImportBatch {SourceInstance=source,Format="CTFd",Fingerprint=new string('a',64),PlanFingerprint=new string('b',64),Title="Synthetic"};
        db.PlatformImportBatches.Add(batch);
        var baseline=new PlatformScoreBaseline {UserId=userId,BatchId=batch.Id,SourceInstance=source,SourceUserId="1",Score=score,SolvedCount=solves,HasActivity=true,CreatedAtUtc=DateTimeOffset.UtcNow.AddDays(-2)};
        db.PlatformScoreBaselines.Add(baseline);return baseline;
    }
    private static Challenge PublishedQuestion(AppDbContext db)
    {
        var question=new Challenge {Type=ChallengeType.StaticAttachment,Score=100,IsEnabled=true,PublicationState=ChallengePublicationState.Published};
        var tree=new SkillTree {Name="Admin score board"};
        var revision=new SkillTreeRevision {Status=SkillTreeRevisionStatus.Published,Categories=[new SkillTreeCategoryRef {Category=new SkillCategory {Name="Web",Skills=[new CategorySkillRef {Skill=new Skill {Name="Web",Contents=[new SkillContent {Challenge=question}]}}]}}]};
        tree.Revisions.Add(revision);db.SkillTrees.Add(tree);db.SaveChanges();tree.CurrentPublishedRevisionId=revision.Id;return question;
    }

}
