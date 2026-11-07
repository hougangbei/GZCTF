using System.Net.Mime;
using System.IO.Compression;
using System.Text.Json;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.ChallengeRuntime.Application;
using GZCTF.Features.ChallengeRuntime.Infrastructure;
using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GZCTF.Utils;
using CanonicalChallenge = GZCTF.Features.ChallengeLibrary.Domain.Challenge;

namespace GZCTF.Features.ChallengeRuntime.Api;

[ApiController]
[Route("api/challenges")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class ChallengesController(
    AppDbContext db,
    UserManager<UserInfo> users,
    ChallengeRuntimeService runtime,
    DynamicAttachmentAllocator attachments,
    ILegacyStorageAdapter storage,
    ChallengeHelpService help,
    ChallengeAccessPolicy access) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ChallengeRuntimeDetailResponse>> Get(
        Guid id, [FromQuery] string? locale, [FromQuery] Guid? course, CancellationToken token)
    {
        var user = await users.GetUserAsync(User);
        if (!await access.CanReadAsync(id, user, token, course))
            return NotFound();
        var challenge = await ReadChallengeAsync(id, user, token, course);
        if (challenge is null)
            return NotFound();
        var text = challenge.Localizations.FirstOrDefault(item =>
                       string.Equals(item.Locale, locale, StringComparison.OrdinalIgnoreCase)) ??
                   challenge.Localizations.FirstOrDefault(item => item.Locale == "en");
        return Ok(new ChallengeRuntimeDetailResponse(
            challenge.Id,
            text?.Title ?? string.Empty,
            text?.Summary ?? string.Empty,
            CourseResourceUrls.ForCourse(text?.Body ?? string.Empty, course),
            challenge.Type.ToString(),
            challenge.CtfCategory.ToString(),
            challenge.Hints.Select(item => item.Locale).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            challenge.Writeups.Count > 0,
            HasConfiguredAttachments(challenge),
            challenge.Type.IsContainer(),
            challenge.Type == ChallengeType.DynamicAttachment && ImportedFiles(challenge).Any(file => !file.DynamicPool && !file.ResourceOnly)));
    }

    [HttpGet("{id:guid}/attachment")]
    [RequireUser]
    public async Task<IActionResult> DownloadAttachment(Guid id, [FromQuery] bool supplementary, [FromQuery] Guid? course, CancellationToken token)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        if (!await access.CanAccessAsync(id, user, token, course))
            return NotFound();
        var imported = await PublishedCourseContentReader.GetChallengeAsync(db, id, token, courseId: course);
        var draftPreview = imported is null && course is null && user.Role == Role.Admin;
        if (draftPreview) imported = await PublishedCourseContentReader.GetChallengeAsync(db, id, token, true);
        if (imported is null) return NotFound();
        if (imported.SourceType is "GZCTF-GAME" or "CTFd" && (imported.Type != ChallengeType.DynamicAttachment || supplementary))
        {
            var files = ImportedFiles(imported).Where(item => !item.ResourceOnly && (!supplementary || !item.DynamicPool)).ToArray();
            if (files.Length == 0)
                return NotFound();
            foreach (var item in files)
            if (item.RemoteUrl is null && !await storage.ExistsAsync(item.StorageKey, token))
                return NotFound();
            if (files.Length == 1 && files[0].RemoteUrl is { } remote)
                return SafeRemote(remote) ? Redirect(remote) : NotFound();
            if (files.Length == 1)
                return File(await storage.OpenReadAsync(files[0].StorageKey, token), MediaTypeNames.Application.Octet, files[0].FileName);
            var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
                for (var i = 0; i < files.Length; i++)
                {
                    var item = files[i];
                    var name = Path.GetFileName(item.FileName.Replace('\\', '/'));
                    await using var destination = zip.CreateEntry($"{i + 1:D2}-{name}{(item.RemoteUrl is null ? "" : ".link.txt")}").Open();
                    if (item.RemoteUrl is { } url)
                    { if (!SafeRemote(url)) return NotFound(); await destination.WriteAsync(System.Text.Encoding.UTF8.GetBytes(url), token); }
                    else
                    { await using var input = await storage.OpenReadAsync(item.StorageKey, token); await input.CopyToAsync(destination, token); }
                }
            output.Position = 0;
            return File(output, "application/zip", "attachments.zip");
        }
        if (supplementary)
            return NotFound();
        var instance = await runtime.GetOrCreateInstanceAsync(user.Id, id, token, draftPreview);
        var assignment = await attachments.GetOrAllocateAsync(user.Id, id, token, draftPreview);
        if (!await storage.ExistsAsync(assignment.EffectiveStorageKey, token))
            return NotFound();
        var stream = await storage.OpenReadAsync(assignment.EffectiveStorageKey, token);
        return File(stream, MediaTypeNames.Application.Octet, assignment.FileName);
    }

    [HttpGet("{id:guid}/resources/{hash}")]
    public async Task<IActionResult> Resource(Guid id, string hash, [FromQuery] Guid? course, CancellationToken token)
    {
        var user = await users.GetUserAsync(User);
        if (!await access.CanReadAsync(id, user, token, course))
            return NotFound();
        var challenge = await ReadChallengeAsync(id, user, token, course);
        if (challenge is null) return NotFound();
        var file = ImportedFiles(challenge).FirstOrDefault(item => !item.DynamicPool && item.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase));
        if (file is null || !await storage.ExistsAsync(file.StorageKey, token))
            return NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        var contentType = file.ContentType is "image/png" or "image/jpeg" or "image/webp" or "image/gif" ? file.ContentType : MediaTypeNames.Application.Octet;
        return File(await storage.OpenReadAsync(file.StorageKey, token), contentType);
    }
    private async Task<CanonicalChallenge?> ReadChallengeAsync(Guid id, UserInfo? user, CancellationToken token, Guid? course) =>
        await PublishedCourseContentReader.GetChallengeAsync(db, id, token, courseId: course) ??
        (user?.Role == Role.Admin && course is null ? await PublishedCourseContentReader.GetChallengeAsync(db, id, token, true) : null);

    private static IReadOnlyList<ImportedFile> ImportedFiles(CanonicalChallenge challenge)
    {
        if (challenge.SourceType is not ("GZCTF-GAME" or "CTFd"))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(challenge.RuntimeConfigurationJson ?? "{}");
            if (doc.RootElement.TryGetProperty("Attachments", out var files))
                return JsonSerializer.Deserialize<List<ImportedFile>>(files.GetRawText()) ?? [];
        }
        catch (JsonException) { }
        return [];
    }
    private sealed record ImportedFile(string FileName, string Sha256, string StorageKey, bool DynamicPool = false, bool ResourceOnly = false, string? RemoteUrl = null, string? ContentType = null);
    private static bool SafeRemote(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);

    [HttpGet("{id:guid}/hints/next")]
    [RequireUser]
    public async Task<ActionResult<ChallengeHintResponse>> NextHint(
        Guid id, [FromQuery] string? locale, [FromQuery] Guid? course, CancellationToken token)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        if (!await access.CanAccessAsync(id, user, token, course))
            return NotFound();
        var response = await help.RevealNextHintAsync(user.Id, id, locale, token, course);
        return response is null ? NotFound() : Ok(response with { Content = CourseResourceUrls.ForCourse(response.Content, course) });
    }

    [HttpGet("{id:guid}/writeup")]
    [RequireUser]
    public async Task<ActionResult<ChallengeWriteupResponse>> Writeup(
        Guid id, [FromQuery] string? locale, [FromQuery] Guid? course, CancellationToken token)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        if (!await access.CanAccessAsync(id, user, token, course))
            return NotFound();
        var response = await help.RevealWriteupAsync(user.Id, id, locale, token, course);
        return response is null ? NotFound() : Ok(response with { Content = CourseResourceUrls.ForCourse(response.Content, course) });
    }

    private static bool HasConfiguredAttachments(CanonicalChallenge challenge)
    {
        if (challenge.SourceType is "GZCTF-GAME" or "CTFd" && challenge.Type != ChallengeType.DynamicAttachment)
            return ImportedFiles(challenge).Any(item => !item.ResourceOnly);
        var metadata = challenge.Flags.FirstOrDefault(flag =>
            flag.Kind == ChallengeFlagKind.DynamicAttachment)?.MetadataJson;
        metadata ??= challenge.RuntimeConfigurationJson;
        if (string.IsNullOrWhiteSpace(metadata))
            return false;
        try
        {
            using var document = JsonDocument.Parse(metadata);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Attachments", out var attachments))
                root = attachments;
            return root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed record ChallengeRuntimeDetailResponse(
    Guid Id,
    string Title,
    string Summary,
    string Body,
    string Type,
    string CtfCategory,
    int HintLocaleCount,
    bool HasWriteup,
    bool HasAttachment,
    bool HasContainer, bool HasSupplementaryAttachment = false);
