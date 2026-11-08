using GZCTF.Integration.Test.Tests.Runtime;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.ChallengeRuntime.Application;
using GZCTF.Features.ChallengeRuntime.Infrastructure;
using GZCTF.Features.Imports.Application;
using GZCTF.Features.Imports.Infrastructure;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using LegacyContainer = GZCTF.Models.Data.Container;

namespace GZCTF.Integration.Test.Tests.Imports;

[Collection(nameof(IntegrationTestCollection))]
public sealed class CtfdWhaleRuntimeTests(GZCTFApplicationFactory factory)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Imported_Whale_runtime_keeps_generation_environment_and_validation_semantics(bool staticFlags, bool missingMode)
    {
        var user = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), "Whale!Synthetic2026");
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        using var archive = Fixture(staticFlags);
        var package = await new PlatformArchiveReader().ReadAsync(archive);
        var importer = services.GetRequiredService<PlatformImportService>();
        var options = new PlatformImportOptions("whale-runtime-" + Guid.NewGuid().ToString("N"));
        var preview = await importer.PreviewAsync(package, options);
        Assert.True(preview.CanImport, string.Join(",", preview.Issues.Where(issue => issue.Severity == "error").Select(issue => issue.Code)));
        await importer.ImportAsync(package, options, preview.PlanFingerprint, user.Id);
        db.ChangeTracker.Clear();
        var id = await db.PlatformImportMaps.Where(map => map.SourceInstance == options.SourceInstance && map.Kind == "challenge")
            .Select(map => map.TargetId).SingleAsync();
        var challenge = await db.Challenges.Include(item => item.Flags).SingleAsync(item => item.Id == id);
        Assert.Equal(staticFlags ? ChallengeType.StaticContainer : ChallengeType.DynamicContainer, challenge.Type);
        Assert.Equal(ChallengePublicationState.Draft, challenge.PublicationState);
        using var settings = JsonDocument.Parse(challenge.RuntimeConfigurationJson!);
        Assert.Equal("ctfd-whale", settings.RootElement.GetProperty("FlagMode").GetString());
        Assert.Equal(5, settings.RootElement.GetProperty("Cpu").GetInt32());
        Assert.Equal(128, settings.RootElement.GetProperty("MemoryMb").GetInt32());
        Assert.Equal(staticFlags ? 2 : 0, challenge.Flags.Count(flag => flag.Kind == ChallengeFlagKind.Static));

        if (missingMode)
        {
            // Older editor saves dropped the mode; immutable import provenance must keep
            // both the runtime alias and the static Flag judging behavior consistent.
            var edited = System.Text.Json.Nodes.JsonNode.Parse(challenge.RuntimeConfigurationJson!)!.AsObject();
            edited.Remove("FlagMode");
            challenge.RuntimeConfigurationJson = edited.ToJsonString();
            await db.SaveChangesAsync();
        }

        // The recording adapter never resolves Docker/Kubernetes or launches a source image.
        var adapter = new RecordingRuntime();
        var runtime = new ChallengeRuntimeService(db, adapter, services.GetRequiredService<LearningInstanceSettingsService>());
        var first = await runtime.StartAsync(user.Id, id, allowDraft: true);
        var firstFlag = first.AssignedFlag;
        AssertWhaleRequest(adapter.LastRequest!, firstFlag);
        await runtime.StopAsync(user.Id, id);
        // End the administrator-only draft test before opening a published learner instance.
        challenge.PublicationState = ChallengePublicationState.Published;
        await db.SaveChangesAsync();
        await PublishedRuntimeSnapshotTests.PublishAsync(db, challenge);
        var second = await runtime.StartAsync(user.Id, id);
        Assert.NotEqual(firstFlag, second.AssignedFlag);
        AssertWhaleRequest(adapter.LastRequest!, second.AssignedFlag);
        Assert.Equal(2, adapter.StartCount);

        var submissions = ActivatorUtilities.CreateInstance<ChallengeSubmissionService>(services, runtime);
        var rejected = await submissions.SubmitAsync(user.Id, id, staticFlags ? second.AssignedFlag! : firstFlag!);
        Assert.False(rejected.Accepted);
        var accepted = await submissions.SubmitAsync(user.Id, id,
            staticFlags ? "fixture-static-alternative" : second.AssignedFlag!);
        Assert.True(accepted.Accepted);
        Assert.True(accepted.FirstSolve);
        Assert.Equal(2, adapter.StopCount); // Explicit restart and successful solve both retire the fake container.
    }

    private static void AssertWhaleRequest(CanonicalContainerRequest request, string? assigned)
    {
        Assert.True(request.UseWhaleFlagEnvironment);
        Assert.Equal("fixture.invalid/whale:synthetic", request.Image);
        Assert.Equal(8080, request.ExposedPort);
        Assert.Equal(5, request.Cpu);
        Assert.Equal(128, request.MemoryMb);
        Assert.Equal(assigned, request.Flag);
        Assert.NotNull(request.Flag);
        Assert.StartsWith("flag{", request.Flag);
        Assert.EndsWith("}", request.Flag);
        Assert.True(Guid.TryParse(request.Flag[5..^1], out _));
        Assert.NotEqual("fixture-static-primary", request.Flag);
        Assert.NotEqual("fixture-static-alternative", request.Flag);
    }

    private static MemoryStream Fixture(bool staticFlags)
    {
        var tables = new Dictionary<string, string>
        {
            ["challenges"] = """[{"id":1,"name":"Synthetic Whale runtime","category":"Web","type":"dynamic_docker","value":100,"state":"visible"}]""",
            ["dynamic_docker_challenge"] = """[{"id":1,"docker_image":"fixture.invalid/whale:synthetic","redirect_port":8080,"redirect_type":"direct","cpu_limit":0.5,"memory_limit":"128m","dynamic_score":0}]""",
            ["flags"] = staticFlags
                ? """[{"id":1,"challenge_id":1,"type":"static","content":"fixture-static-primary"},{"id":2,"challenge_id":1,"type":"static","content":"fixture-static-alternative"}]"""
                : "[]"
        };
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var (table, rows) in tables)
            {
                using var output = zip.CreateEntry($"db/{table}.json").Open();
                output.Write(Encoding.UTF8.GetBytes("{\"results\":" + rows + "}"));
            }
        stream.Position = 0;
        return stream;
    }

    private sealed class RecordingRuntime : ILegacyContainerRuntimeAdapter
    {
        public CanonicalContainerRequest? LastRequest { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public Task<LegacyContainer?> StartAsync(CanonicalContainerRequest request, CancellationToken token)
        {
            LastRequest = request;
            StartCount++;
            return Task.FromResult<LegacyContainer?>(new LegacyContainer
            {
                Id = Guid.NewGuid(), ContainerId = "synthetic-whale-" + Guid.NewGuid().ToString("N"),
                Image = request.Image, IP = "127.0.0.1", Port = request.ExposedPort,
                Status = ContainerStatus.Running
            });
        }

        public Task StopAsync(LegacyContainer container, CancellationToken token)
        {
            StopCount++;
            container.Status = ContainerStatus.Destroyed;
            return Task.CompletedTask;
        }
    }
}
