using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using CanonicalChallenge = GZCTF.Features.ChallengeLibrary.Domain.Challenge;

namespace GZCTF.Integration.Test.Tests.Imports;

[Collection(nameof(IntegrationTestCollection))]
public sealed class PlatformImportResourceTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Dynamic_attachment_allocates_only_flag_files_and_keeps_supplementary_body_resources()
    {
        var (admin, userId) = await AdminAsync();
        using var client = admin;
        var assigned = Bytes("assigned fixture attachment");
        var supplementary = Bytes("supplementary fixture download");
        var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5WQAAAAASUVORK5CYII=");
        var assignedHash = Hash(assigned);
        var imageHash = Hash(image);
        var archive = Gzctf(new
        {
            id = 1,
            title = "Dynamic attachment resources",
            category = "Misc",
            type = "DynamicAttachment",
            enabled = true,
            content = $"![illustration](/assets/{imageHash})",
            scoring = new { original = 100 },
            flags = new { @static = new[] { new { value = "fixture-assigned-flag", attachment = Local(assigned, "assigned.bin") } } },
            attachment = Local(supplementary, "supplement.txt")
        }, new() { [$"files/{assignedHash}"] = assigned, [$"files/{Hash(supplementary)}"] = supplementary, [$"files/{imageHash}"] = image });
        var (challengeId, _) = await ImportAsync(client, archive);
        var challenge = await ChallengeAsync(challengeId);
        using var pool = JsonDocument.Parse(challenge.Flags.Single(flag => flag.Kind == ChallengeFlagKind.DynamicAttachment).MetadataJson!);
        var candidate = Assert.Single(pool.RootElement.EnumerateArray());
        Assert.Equal(assignedHash, candidate.GetProperty("Sha256").GetString());
        Assert.Equal("assigned.bin", candidate.GetProperty("FileName").GetString());
        Assert.Equal("fixture-assigned-flag", candidate.GetProperty("Flag").GetString());
        using var runtime = JsonDocument.Parse(challenge.RuntimeConfigurationJson!);
        var resources = runtime.RootElement.GetProperty("Attachments").EnumerateArray().ToArray();
        Assert.Equal(3, resources.Length);
        Assert.Single(resources.Where(resource => resource.GetProperty("DynamicPool").GetBoolean()));
        Assert.Single(resources.Where(resource => resource.GetProperty("ResourceOnly").GetBoolean()));

        using var download = await client.GetAsync($"/api/challenges/{challengeId}/attachment");
        download.EnsureSuccessStatusCode();
        Assert.Equal(assigned, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("assigned.bin", download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        using var detail = await client.GetAsync($"/api/challenges/{challengeId}");
        detail.EnsureSuccessStatusCode();
        Assert.True((await detail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hasSupplementaryAttachment").GetBoolean());
        using var extra = await client.GetAsync($"/api/challenges/{challengeId}/attachment?supplementary=true");
        extra.EnsureSuccessStatusCode();
        Assert.Equal(supplementary, await extra.Content.ReadAsByteArrayAsync());
        using var repeated = await client.GetAsync($"/api/challenges/{challengeId}/attachment");
        repeated.EnsureSuccessStatusCode();
        Assert.Equal(assigned, await repeated.Content.ReadAsByteArrayAsync());
        using var bodyResource = await client.GetAsync($"/api/challenges/{challengeId}/resources/{imageHash}");
        bodyResource.EnsureSuccessStatusCode();
        Assert.Equal(image, await bodyResource.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/png", bodyResource.Content.Headers.ContentType?.MediaType);
        using var privatePoolResource = await client.GetAsync($"/api/challenges/{challengeId}/resources/{assignedHash}");
        Assert.Equal(HttpStatusCode.NotFound, privatePoolResource.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instance = await db.UserChallengeInstances.SingleAsync(item => item.ChallengeId == challengeId && item.UserId == userId);
        Assert.Equal(assignedHash, instance.AssignedAttachmentSha256);
        Assert.Equal("fixture-assigned-flag", instance.AssignedFlag);
    }

    [Fact]
    public async Task Ctfd_multiple_explicit_attachments_remain_downloadable_when_body_also_references_them()
    {
        var (admin, _) = await AdminAsync();
        using var client = admin;
        var archive = Ctfd("![first](/files/folder/one.txt) [second](/files/folder/two.txt)", new()
        {
            ["uploads/folder/one.txt"] = Bytes("first fixture attachment"),
            ["uploads/folder/two.txt"] = Bytes("second fixture attachment")
        }, ["folder/one.txt", "folder/two.txt"]);
        var (challengeId, _) = await ImportAsync(client, archive);
        var challenge = await ChallengeAsync(challengeId);
        using var runtime = JsonDocument.Parse(challenge.RuntimeConfigurationJson!);
        Assert.All(runtime.RootElement.GetProperty("Attachments").EnumerateArray(), resource =>
            Assert.False(resource.GetProperty("ResourceOnly").GetBoolean()));
        using var response = await client.GetAsync($"/api/challenges/{challengeId}/attachment");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
        Assert.Equal(2, zip.Entries.Count);
        var contents = new Dictionary<string, string>();
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            contents[entry.FullName] = await reader.ReadToEndAsync();
        }
        Assert.Equal("first fixture attachment", contents["01-one.txt"]);
        Assert.Equal("second fixture attachment", contents["02-two.txt"]);
    }

    [Theory]
    [InlineData("GZCTF-GAME")]
    [InlineData("CTFd")]
    public async Task Existing_download_body_aliases_all_rewrite_to_one_imported_resource(string format)
    {
        var (admin, _) = await AdminAsync();
        using var client = admin;
        var payload = Bytes("shared body and download fixture");
        var hash = Hash(payload);
        var relative = format == "CTFd" ? "/files/folder/shared.txt" : "/Assets/" + hash + "/fixture.png";
        var absolute = "https://fixture.example.invalid" + (format == "CTFd" ? "" : "/api") + relative + "?inline=true";
        var body = $"![first]({relative}) ![second]({absolute}) [again]({relative})";
        var archive = format == "CTFd"
            ? Ctfd(body, new() { ["uploads/folder/shared.txt"] = payload }, ["folder/shared.txt"])
            : Gzctf(new
            {
                id = 1,
                title = "Aliased body asset",
                type = "StaticAttachment",
                category = "Misc",
                enabled = true,
                content = body,
                scoring = new { original = 100 },
                attachment = Local(payload, "shared.txt"),
                flags = new { @static = new[] { new { value = "fixture-alias-flag" } } }
            }, new() { [$"files/{hash}"] = payload });
        var (challengeId, _) = await ImportAsync(client, archive);
        var expectedUrl = $"/api/challenges/{challengeId}/resources/{hash}";
        var expectedBody = $"![first]({expectedUrl}) ![second]({expectedUrl}) [again]({expectedUrl})";
        var challenge = await ChallengeAsync(challengeId);
        Assert.All(challenge.Localizations, text => Assert.Equal(expectedBody, text.Body));
        using var detail = await client.GetAsync($"/api/challenges/{challengeId}?locale=en");
        detail.EnsureSuccessStatusCode();
        Assert.Equal(expectedBody, (await detail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("body").GetString());
        using var resource = await client.GetAsync(expectedUrl);
        resource.EnsureSuccessStatusCode();
        Assert.Equal(payload, await resource.Content.ReadAsByteArrayAsync());
        using var download = await client.GetAsync($"/api/challenges/{challengeId}/attachment");
        download.EnsureSuccessStatusCode();
        Assert.Equal(payload, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Remote_static_attachment_warns_and_redirects_without_fetching_source()
    {
        await using var origin = new DownloadProbe();
        var (admin, _) = await AdminAsync();
        using var client = admin;
        var archive = Gzctf(new
        {
            id = 1,
            title = "External fixture attachment",
            type = "StaticAttachment",
            category = "Misc",
            enabled = true,
            content = "External source archive",
            scoring = new { original = 100 },
            attachment = new { type = "Remote", remoteUrl = origin.Url, fileName = "external.bin" },
            flags = new { @static = new[] { new { value = "fixture-remote-flag" } } }
        }, []);
        var (challengeId, preview) = await ImportAsync(client, archive);
        Assert.Contains(preview.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "remote_attachment_link_preserved" &&
            issue.GetProperty("severity").GetString() == "warning");
        using var response = await client.GetAsync($"/api/challenges/{challengeId}/attachment");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(origin.Url, response.Headers.Location?.AbsoluteUri);
        Assert.Equal(0, origin.RequestCount);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.UserChallengeInstances.AnyAsync(instance => instance.ChallengeId == challengeId));
    }

    private async Task<(HttpClient Client, Guid UserId)> AdminAsync()
    {
        var name = TestDataSeeder.RandomName();
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, name, "ImportResources!12345", role: Role.Admin);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        (await client.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = name, Password = "ImportResources!12345" })).EnsureSuccessStatusCode();
        return (client, user.Id);
    }

    private async Task<(Guid ChallengeId, JsonElement Preview)> ImportAsync(HttpClient client, byte[] archive)
    {
        var source = "resource-" + Guid.NewGuid().ToString("N");
        using var preflight = await client.PostAsync("/api/admin/platform-imports/preview", Upload(archive, source));
        preflight.EnsureSuccessStatusCode();
        var preview = await preflight.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(preview.GetProperty("canImport").GetBoolean(), preview.GetProperty("issues").GetRawText());
        using var executed = await client.PostAsync("/api/admin/platform-imports", Upload(archive, source, preview.GetProperty("planFingerprint").GetString()));
        executed.EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.PlatformImportMaps.Where(map => map.SourceInstance == source && map.Kind == "challenge")
            .Select(map => map.TargetId).SingleAsync(), preview);
    }

    private async Task<CanonicalChallenge> ChallengeAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Challenges.AsNoTracking()
            .Include(challenge => challenge.Flags).Include(challenge => challenge.Localizations).SingleAsync(challenge => challenge.Id == id);
    }

    private static MultipartFormDataContent Upload(byte[] archive, string source, string? plan = null)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(archive), "package", "fixture.zip");
        form.Add(new StringContent(JsonSerializer.Serialize(new { sourceInstance = source })), "options");
        if (plan is not null)
            form.Add(new StringContent(plan), "planFingerprint");
        return form;
    }

    private static byte[] Gzctf(object challenge, Dictionary<string, byte[]> files)
    {
        var entries = new Dictionary<string, byte[]>(files)
        {
            ["manifest.json"] = Json(new { format = "GZCTF-GAME", version = "1.0", statistics = new { challengeCount = 1 } }),
            ["game.json"] = Json(new { title = "Resource regression fixture" }),
            ["challenges/1.json"] = Json(challenge)
        };
        return Zip(entries);
    }

    private static byte[] Ctfd(string body, Dictionary<string, byte[]> files, string[] locations)
    {
        var entries = new Dictionary<string, byte[]>(files)
        {
            ["db/challenges.json"] = Json(new { results = new[] { new { id = 1, name = "CTFd resources", type = "standard", category = "Misc", state = "visible", value = 100, description = body } } }),
            ["db/flags.json"] = Json(new { results = new[] { new { id = 1, challenge_id = 1, type = "static", content = "fixture-ctfd-flag" } } }),
            ["db/files.json"] = Json(new { results = locations.Select((location, index) => new { id = index + 1, challenge_id = 1, location }).ToArray() })
        };
        return Zip(entries);
    }

    private static byte[] Zip(Dictionary<string, byte[]> entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var (path, bytes) in entries)
            {
                using var output = zip.CreateEntry(path).Open();
                output.Write(bytes);
            }
        return stream.ToArray();
    }

    private static object Local(byte[] bytes, string fileName) => new { type = "Local", hash = Hash(bytes), fileName, fileSize = bytes.Length };
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    private static byte[] Json(object value) => JsonSerializer.SerializeToUtf8Bytes(value);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class DownloadProbe : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task server;
        private int count;
        public string Url { get; }
        public int RequestCount => Volatile.Read(ref count);

        public DownloadProbe()
        {
            listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/external.bin";
            server = ServeAsync();
        }

        private async Task ServeAsync()
        {
            try
            {
                while (true)
                {
                    using var connection = await listener.AcceptTcpClientAsync(cancellation.Token);
                    Interlocked.Increment(ref count);
                    await using var stream = connection.GetStream();
                    await stream.WriteAsync(Bytes("HTTP/1.1 200 OK\r\nContent-Length: 7\r\nConnection: close\r\n\r\nfixture"), cancellation.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) when (cancellation.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            await cancellation.CancelAsync();
            listener.Stop();
            await server;
            cancellation.Dispose();
        }
    }
}
