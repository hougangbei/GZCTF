using GZCTF.Features.LearningPaths.Domain;
using GZCTF.Features.LearningPaths.Application;
using GZCTF.Features.LearningProgress.Application;
using GZCTF.Storage.Interface;
using GZCTF.Features.SkillTrees.Api;
using GZCTF.Models.Request.Account;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.ChallengeRuntime.Application;
using GZCTF.Features.ChallengeRuntime.Domain;
using GZCTF.Features.ChallengeRuntime.Infrastructure;
using GZCTF.Features.Leaderboard.Application;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Challenge = GZCTF.Features.ChallengeLibrary.Domain.Challenge;

namespace GZCTF.Integration.Test.Tests.Runtime;

[Collection(nameof(IntegrationTestCollection))]
public sealed class PublishedRuntimeSnapshotTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Library_draft_edits_cannot_change_published_body_help_flag_score_or_access()
    {
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Snapshot!2026");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var challenge = NewChallenge();
        db.Challenges.Add(challenge);
        await db.SaveChangesAsync();
        var tree = await PublishAsync(db, challenge);
        challenge.Localizations[0].Title = "Private title";
        challenge.Localizations[0].Body = "Private body";
        challenge.Flags[0].Value = "flag{private}";
        challenge.Score = 9999;
        challenge.IsEnabled = false;
        challenge.PublicationState = ChallengePublicationState.Draft;
        db.ChallengeHints.RemoveRange(challenge.Hints);
        challenge.Hints = [];
        challenge.Writeups[0].Content = "Private writeup";
        await db.SaveChangesAsync();

        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/challenges/{challenge.Id}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Published title", body.GetProperty("title").GetString());
        Assert.Equal("Published body", body.GetProperty("body").GetString());
        var help = scope.ServiceProvider.GetRequiredService<ChallengeHelpService>();
        Assert.Equal("Published hint", (await help.RevealNextHintAsync(user.Id, challenge.Id, "en"))?.Content);
        Assert.Equal("Published writeup", (await help.RevealWriteupAsync(user.Id, challenge.Id, "en"))?.Content);
        var submissions = scope.ServiceProvider.GetRequiredService<ChallengeSubmissionService>();
        Assert.False((await submissions.SubmitAsync(user.Id, challenge.Id, "flag{private}")).Accepted);
        Assert.True((await submissions.SubmitAsync(user.Id, challenge.Id, "flag{published}")).FirstSolve);
        var score = (await scope.ServiceProvider.GetRequiredService<RankingScoreService>().SnapshotAsync(default))
            .Single(s => s.User.UserId == user.Id);
        Assert.Equal(120, score.User.Score);
        Assert.Equal(1, score.User.EarnedSolvedCount);
        var draftOnly = NewChallenge();
        db.Challenges.Add(draftOnly);
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/challenges/{draftOnly.Id}")).StatusCode);
        Assert.Null(await PublishedCourseContentReader.GetChallengeAsync(db, draftOnly.Id, default));
        // Removing the published tree removes eligibility without changing stored learner progress.
        tree.DeletedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        Assert.Equal(0, (await scope.ServiceProvider.GetRequiredService<RankingScoreService>().SnapshotAsync(default))
            .Single(s => s.User.UserId == user.Id).User.Score);
        Assert.Equal(1, await db.ChallengeProgress.CountAsync(p => p.UserId == user.Id && p.ChallengeId == challenge.Id));
    }

    [Fact]
    public async Task Shared_content_id_has_one_progress_record_and_one_scored_solve_across_two_courses()
    {
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Snapshot!2026");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var challenge = NewChallenge();
        db.Challenges.Add(challenge);
        await db.SaveChangesAsync();
        await PublishAsync(db, challenge);
        await PublishAsync(db, challenge);
        var service = scope.ServiceProvider.GetRequiredService<ChallengeSubmissionService>();
        Assert.True((await service.SubmitAsync(user.Id, challenge.Id, "flag{published}")).FirstSolve);
        Assert.False((await service.SubmitAsync(user.Id, challenge.Id, "flag{published}")).FirstSolve);
        var score = (await scope.ServiceProvider.GetRequiredService<RankingScoreService>().SnapshotAsync(default))
            .Single(s => s.User.UserId == user.Id);
        Assert.Equal(120, score.User.Score);
        Assert.Equal(1, score.User.EarnedSolvedCount);
        Assert.Equal(1, await db.ChallengeProgress.CountAsync(p => p.UserId == user.Id && p.ChallengeId == challenge.Id));
    }

    [Fact]
    public async Task A_pending_attempt_uses_newly_published_flags_without_retaining_an_unstarted_configuration()
    {
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Snapshot!2026");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var challenge = NewChallenge();
        db.Challenges.Add(challenge);
        await db.SaveChangesAsync();
        var tree = await PublishAsync(db, challenge);
        var submissions = scope.ServiceProvider.GetRequiredService<ChallengeSubmissionService>();
        Assert.False((await submissions.SubmitAsync(user.Id, challenge.Id, "wrong")).Accepted);
        challenge.Flags[0].Value = "flag{newly-published}";
        await db.SaveChangesAsync();
        await PublishAsync(db, challenge, tree);
        Assert.False((await submissions.SubmitAsync(user.Id, challenge.Id, "flag{published}")).Accepted);
        Assert.True((await submissions.SubmitAsync(user.Id, challenge.Id, "flag{newly-published}")).FirstSolve);
    }

    [Theory]
    [InlineData(ChallengeType.StaticContainer)]
    [InlineData(ChallengeType.DynamicContainer)]
    public async Task Running_instance_keeps_startup_flag_and_image_after_republish(ChallengeType type)
    {
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Snapshot!2026");
        var newcomer = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Snapshot!2026");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var challenge = NewChallenge();
        challenge.Type = type;
        challenge.Flags = type == ChallengeType.StaticContainer
            ? [new ChallengeFlag { Kind = ChallengeFlagKind.Static, Value = "flag{old}" }]
            : [new ChallengeFlag { Kind = ChallengeFlagKind.Template, Template = "flag{old-[GUID]}" }];
        challenge.RuntimeConfigurationJson = Configuration("old-image");
        db.Challenges.Add(challenge);
        await db.SaveChangesAsync();
        var tree = await PublishAsync(db, challenge);
        var adapter = new RecordingContainers();
        var runtime = new ChallengeRuntimeService(db, adapter,
            scope.ServiceProvider.GetRequiredService<LearningInstanceSettingsService>());
        var running = await runtime.StartAsync(user.Id, challenge.Id);
        var originalFlag = running.AssignedFlag!;
        if (type == ChallengeType.StaticContainer) Assert.Equal("flag{old}", originalFlag);
        else Assert.StartsWith("flag{old-", originalFlag);
        challenge.Flags[0].Value = "flag{new}";
        challenge.Flags[0].Template = "flag{new-[GUID]}";
        challenge.RuntimeConfigurationJson = Configuration("new-image");
        await db.SaveChangesAsync();
        await PublishAsync(db, challenge, tree);
        var extended = await runtime.ExtendAsync(user.Id, challenge.Id);
        Assert.Equal(running.Id, extended.Id);
        Assert.Equal("old-image", PublishedCourseContentReader.ReadChallenge(extended.PublishedChallengeJson!).RuntimeConfigurationJson is { } config
            ? JsonDocument.Parse(config).RootElement.GetProperty("Image").GetString() : null);
        var second = await runtime.StartAsync(newcomer.Id, challenge.Id);
        if (type == ChallengeType.StaticContainer) Assert.Equal("flag{new}", second.AssignedFlag);
        else Assert.StartsWith("flag{new-", second.AssignedFlag);
        var stillRunning = await runtime.StartAsync(user.Id, challenge.Id);
        Assert.Equal(running.Id, stillRunning.Id);
        Assert.Equal(originalFlag, stillRunning.AssignedFlag);
        Assert.Equal(new[] { "old-image", "new-image" }, adapter.Requests.Select(r => r.Image));
        // Submission service uses its normal runtime but the durable startup payload/flag controls validation.
        Assert.True((await scope.ServiceProvider.GetRequiredService<ChallengeSubmissionService>()
            .SubmitAsync(user.Id, challenge.Id, originalFlag)).Accepted);
        Assert.True((await scope.ServiceProvider.GetRequiredService<ChallengeSubmissionService>()
            .SubmitAsync(newcomer.Id, challenge.Id, second.AssignedFlag!)).Accepted);
    }

    [Fact]
    public async Task Admin_course_test_start_uses_saved_workspace_configuration_and_checks_context()
    {
        const string password = "Snapshot!2026";
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password, role: Role.Admin);
        var learner = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password);
        Guid challengeId, treeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var challenge = NewChallenge();
            challenge.Type = ChallengeType.StaticContainer;
            challenge.RuntimeConfigurationJson = Configuration("published-image");
            db.Challenges.Add(challenge);
            await db.SaveChangesAsync();
            var tree = await PublishAsync(db, challenge);
            challengeId = challenge.Id; treeId = tree.Id;
            var draft = PublishedCourseContentReader.ReadChallenge(PublishedCourseContentReader.FreezeChallenge(challenge));
            draft.RuntimeConfigurationJson = Configuration("workspace-image");
            draft.Flags[0].Value = "flag{workspace}";
            challenge.RuntimeConfigurationJson = Configuration("library-image");
            var document = new CourseDocument(tree.Name, "", "web", null,
                [new CourseCategory(Guid.NewGuid(), "Category", "", "web",
                    [new CourseSkill(Guid.NewGuid(), "Skill", "", "web", true,
                        [new CourseContent(challenge.Id, "challenge", "Title", "", "Draft")])])]);
            db.CourseAuthoringWorkspaces.Add(new CourseAuthoringWorkspace { SkillTreeId = treeId,
                DocumentJson = JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
            db.CourseWorkspaceContents.Add(new CourseWorkspaceContent { SkillTreeId = treeId,
                ContentId = challengeId, Kind = "challenge", PayloadJson = PublishedCourseContentReader.FreezeChallenge(draft) });
            await db.SaveChangesAsync();
        }
        var recording = new RecordingContainers();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILegacyContainerRuntimeAdapter>();
            services.AddSingleton<ILegacyContainerRuntimeAdapter>(recording);
        }));
        using var client = host.CreateClient();
        (await client.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = admin.UserName, Password = password })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/challenges/{challengeId}/instances?course={Guid.NewGuid()}", null)).StatusCode);
        var started = await client.PostAsync($"/api/challenges/{challengeId}/instances?course={treeId}", null);
        started.EnsureSuccessStatusCode();
        var request = Assert.Single(recording.Requests);
        Assert.Equal("workspace-image", request.Image);
        Assert.Equal("flag{workspace}", request.Flag);
        var attemptedSolve = await client.PostAsJsonAsync($"/api/challenges/{challengeId}/submissions", new { flag = "flag{workspace}" });
        attemptedSolve.EnsureSuccessStatusCode();
        var rejection = await attemptedSolve.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(rejection.GetProperty("accepted").GetBoolean());
        Assert.Equal("challenge.test_instance", rejection.GetProperty("rejectionCode").GetString());
        await using (var verify = factory.Services.CreateAsyncScope())
            Assert.False(await verify.ServiceProvider.GetRequiredService<AppDbContext>().ChallengeProgress.AnyAsync(p => p.UserId == admin.Id));
        (await client.DeleteAsync($"/api/challenges/{challengeId}/instances")).EnsureSuccessStatusCode();
        using var learnerClient = host.CreateClient();
        (await learnerClient.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = learner.UserName, Password = password })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await learnerClient.PostAsync($"/api/challenges/{challengeId}/instances?course={treeId}", null)).StatusCode);
        // Without a course parameter an administrator explicitly tests the canonical library draft.
        (await client.PostAsync($"/api/challenges/{challengeId}/instances", null)).EnsureSuccessStatusCode();
        Assert.Equal("library-image", recording.Requests[1].Image);
        (await client.DeleteAsync($"/api/challenges/{challengeId}/instances")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Public_course_context_selects_its_own_published_text_and_never_falls_back_to_another_course()
    {
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Snapshot!2026");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var challenge = NewChallenge();
        var hash = new string('a', 64);
        challenge.SourceType = "CTFd";
        challenge.RuntimeConfigurationJson = JsonSerializer.Serialize(new { Attachments = new[] {
            new { FileName = "resource.png", Sha256 = hash, StorageKey = "published-resource-" + challenge.Id, ResourceOnly = true, ContentType = "image/png" } } });
        challenge.Localizations[0].Body = $"Course A [resource](/api/challenges/{challenge.Id}/resources/{hash})";
        db.Challenges.Add(challenge);
        await db.SaveChangesAsync();
        var courseA = await PublishAsync(db, challenge);
        challenge.Localizations[0].Title = "Course B title";
        challenge.Localizations[0].Body = "Course B body";
        db.ChallengeHints.RemoveRange(challenge.Hints);
        challenge.Hints = [new ChallengeHint { ChallengeId = challenge.Id, Locale = "en", Content = "Course B hint" }];
        challenge.Writeups[0].Content = "Course B writeup";
        // Persist only library prose; snapshot B may legitimately differ in presentation.
        var courseB = await PublishAsync(db, challenge);
        using var client = factory.CreateClient();
        var textA = await client.GetFromJsonAsync<JsonElement>($"/api/challenges/{challenge.Id}?course={courseA.Id}");
        var textB = await client.GetFromJsonAsync<JsonElement>($"/api/challenges/{challenge.Id}?course={courseB.Id}");
        Assert.Equal("Published title", textA.GetProperty("title").GetString());
        Assert.Contains($"?course={courseA.Id}", textA.GetProperty("body").GetString());
        Assert.Equal("Course B title", textB.GetProperty("title").GetString());
        Assert.Equal("Course B body", textB.GetProperty("body").GetString());
        var help = scope.ServiceProvider.GetRequiredService<ChallengeHelpService>();
        Assert.Equal("Published hint", (await help.RevealNextHintAsync(user.Id, challenge.Id, "en", courseId: courseA.Id))?.Content);
        Assert.Equal("Course B hint", (await help.RevealNextHintAsync(user.Id, challenge.Id, "en", courseId: courseB.Id))?.Content);
        Assert.Equal("Published writeup", (await help.RevealWriteupAsync(user.Id, challenge.Id, "en", courseId: courseA.Id))?.Content);
        Assert.Equal("Course B writeup", (await help.RevealWriteupAsync(user.Id, challenge.Id, "en", courseId: courseB.Id))?.Content);
        var unknownCourse = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/challenges/{challenge.Id}?course={unknownCourse}")).StatusCode);
        Assert.Null(await PublishedCourseContentReader.GetChallengeAsync(db, challenge.Id, default, courseId: unknownCourse));
        await using var resource = new MemoryStream([1, 2, 3]);
        await scope.ServiceProvider.GetRequiredService<IBlobStorage>().WriteAsync("published-resource-" + challenge.Id, resource);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/challenges/{challenge.Id}/resources/{hash}?course={courseA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/challenges/{challenge.Id}/resources/{hash}?course={unknownCourse}")).StatusCode);
    }

    [Fact]
    public async Task Published_lessons_survive_draft_edits_and_share_one_completion_across_courses()
    {
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), "Snapshot!2026");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lesson = new Lesson { PublicationState = LessonPublicationState.Published,
            Localizations = [new LessonLocalization { Locale = "en", Title = "Lesson A", Body = "Body A" }] };
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();
        var courseA = await PublishLessonAsync(db, lesson);
        lesson.Localizations[0].Title = "Lesson B";
        lesson.Localizations[0].Body = "Body B";
        await db.SaveChangesAsync();
        var courseB = await PublishLessonAsync(db, lesson);
        lesson.PublicationState = LessonPublicationState.Draft;
        lesson.Localizations[0].Title = "Private title";
        lesson.Localizations[0].Body = "Private body";
        await db.SaveChangesAsync();
        var lessons = scope.ServiceProvider.GetRequiredService<EnrollmentService>();
        Assert.Equal("Body A", (await lessons.GetLessonAsync(lesson.Id, "en", default, courseA.Id)).Body);
        Assert.Equal("Body B", (await lessons.GetLessonAsync(lesson.Id, "en", default, courseB.Id)).Body);
        await Assert.ThrowsAsync<LearningLessonNotFoundException>(() => lessons.GetLessonAsync(lesson.Id, "en", default, Guid.NewGuid()));
        var progress = scope.ServiceProvider.GetRequiredService<LessonProgressService>();
        await progress.CompleteAsync(user.Id, lesson.Id, default, courseA.Id);
        await progress.CompleteAsync(user.Id, lesson.Id, default, courseB.Id);
        Assert.Equal(1, await db.LessonProgress.CountAsync(p => p.UserId == user.Id && p.LessonId == lesson.Id));
        var records = await scope.ServiceProvider.GetRequiredService<LearningRecordService>().GetAsync(user.Id, "en", default);
        Assert.Equal("Lesson A", Assert.Single(records.RecentActivity, a => a.ContentId == lesson.Id).Title);
    }

    private static async Task<SkillTree> PublishLessonAsync(AppDbContext db, Lesson lesson)
    {
        var tree = new SkillTree { Name = "Frozen lesson course" };
        var revision = new SkillTreeRevision { SkillTree = tree, Status = SkillTreeRevisionStatus.Published };
        db.SkillTreeRevisions.Add(revision);
        await db.SaveChangesAsync();
        var document = new CourseDocument(tree.Name, "", "web", null,
            [new CourseCategory(Guid.NewGuid(), "Category", "", "web",
                [new CourseSkill(Guid.NewGuid(), "Skill", "", "web", true,
                    [new CourseContent(lesson.Id, "lesson", lesson.Localizations[0].Title, "", "Published")])])]);
        db.CoursePublishedVersions.Add(new CoursePublishedVersion { Id = revision.Id, SkillTreeId = tree.Id,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublicDocumentJson = JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Contents = [new CoursePublishedContent { Id = Guid.NewGuid(), ContentId = lesson.Id, Kind = "lesson",
                PayloadJson = JsonSerializer.Serialize(new { lesson = new { lesson.Id, lesson.CreatedAtUtc, lesson.PublicationState },
                    localizations = lesson.Localizations.Select(l => new { l.Id, l.LessonId, l.Locale, l.Title, l.Body }) }) }] });
        tree.CurrentPublishedRevisionId = revision.Id;
        await db.SaveChangesAsync();
        return tree;
    }

    private static Challenge NewChallenge() => new()
    {
        PublicationState = ChallengePublicationState.Published, Score = 120,
        Localizations = [new ChallengeLocalization { Locale = "en", Title = "Published title", Body = "Published body" }],
        Flags = [new ChallengeFlag { Kind = ChallengeFlagKind.Static, Value = "flag{published}" }],
        Hints = [new ChallengeHint { Locale = "en", Content = "Published hint" }],
        Writeups = [new ChallengeWriteup { Locale = "en", Content = "Published writeup" }]
    };

    private static string Configuration(string image) => JsonSerializer.Serialize(new
        { Image = image, ExposedPort = 80, Cpu = 1, MemoryMb = 64, StorageMb = 128, NetworkMode = "Open" });

    internal static async Task<SkillTree> PublishAsync(AppDbContext db, Challenge challenge, SkillTree? tree = null)
    {
        tree ??= new SkillTree { Name = "Frozen runtime course" };
        if (db.Entry(tree).State == EntityState.Detached) db.SkillTrees.Add(tree);
        var revision = new SkillTreeRevision { SkillTree = tree, Status = SkillTreeRevisionStatus.Published };
        db.SkillTreeRevisions.Add(revision);
        await db.SaveChangesAsync();
        db.CoursePublishedVersions.Add(new CoursePublishedVersion
        {
            Id = revision.Id, SkillTreeId = tree.Id, PublishedAtUtc = DateTimeOffset.UtcNow,
            PublicDocumentJson = "{\"categories\":[]}",
            Contents = [new CoursePublishedContent { Id = Guid.NewGuid(), ContentId = challenge.Id,
                Kind = "challenge", PayloadJson = PublishedCourseContentReader.FreezeChallenge(challenge) }]
        });
        tree.CurrentPublishedRevisionId = revision.Id;
        await db.SaveChangesAsync();
        return tree;
    }

    private sealed class RecordingContainers : ILegacyContainerRuntimeAdapter
    {
        public List<CanonicalContainerRequest> Requests { get; } = [];
        public Task<Container?> StartAsync(CanonicalContainerRequest request, CancellationToken token)
        {
            Requests.Add(request);
            return Task.FromResult<Container?>(new Container { ContainerId = Guid.NewGuid().ToString(),
                Image = request.Image, Status = ContainerStatus.Running });
        }
        public Task StopAsync(Container container, CancellationToken token)
        { container.Status = ContainerStatus.Destroyed; return Task.CompletedTask; }
    }
}
