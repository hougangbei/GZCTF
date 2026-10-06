using System.Data;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using GZCTF.Features.AboutPage.Domain;
using GZCTF.Features.Auditing.Application;
using GZCTF.Middlewares;
using GZCTF.Repositories.Interface;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.AboutPage.Api;

[ApiController]
public sealed class AboutPageController(AppDbContext db, IBlobRepository blobs)
    : ControllerBase
{

    [HttpGet("api/about")]
    public async Task<IActionResult> Published(CancellationToken token)
    {
        var state = await db.AboutPageStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, token);
        if (state?.CurrentVersionId is not { } id) return Ok(new { published = false });
        var version = await db.AboutPageVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        if (version is null) return Ok(new { published = false });
        var normalized = AboutDocumentValidator.Normalize(version.DocumentJson);
        if (normalized.Error is not null) return Problem("Published about page uses an unsupported document schema.", statusCode: StatusCodes.Status500InternalServerError);
        var etag = $"\"about-{version.Id:N}\"";
        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "no-cache,must-revalidate";
        if (Request.Headers.IfNoneMatch.Any(value => value == etag)) return StatusCode(304);
        using var document = JsonDocument.Parse(version.DocumentJson);
        return Ok(new { published = true, version = version.VersionNumber, publishedAtUtc = version.PublishedAtUtc, document = document.RootElement.Clone() });
    }

    [RequireAdmin]
    [HttpGet("api/admin/about")]
    public async Task<IActionResult> AdminState(CancellationToken token)
    {
        var state = await GetState(token);
        return Ok(new { document = state.DraftJson, revision = state.DraftRevision,
            lockOwnerId = state.LockOwnerId, lockOwnerName = state.LockOwnerName,
            lockCreatedAtUtc = state.LockCreatedAtUtc, canEdit = state.LockOwnerId == ActorId,
            publishedVersionId = state.CurrentVersionId, updatedAtUtc = state.DraftUpdatedAtUtc });
    }

    [RequireAdmin, AuditAction("about.lock.acquire")]
    [HttpPost("api/admin/about/lock")]
    public async Task<IActionResult> AcquireLock(CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var state = await GetState(token);
        if (state.LockOwnerId is not null && state.LockOwnerId != ActorId)
            return Conflict(new { error = "lock_occupied", ownerName = state.LockOwnerName, createdAtUtc = state.LockCreatedAtUtc });
        if (state.LockOwnerId is null)
        {
            state.LockOwnerId = ActorId;
            state.LockOwnerName = User.Identity?.Name ?? "管理员";
            state.LockCreatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(token);
        }
        await tx.CommitAsync(token);
        return Ok(new { acquired = true, ownerName = state.LockOwnerName, createdAtUtc = state.LockCreatedAtUtc });
    }

    [RequireAdmin, AuditAction("about.lock.release")]
    [HttpDelete("api/admin/about/lock")]
    public async Task<IActionResult> ReleaseLock(CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var state = await GetState(token);
        if (state.LockOwnerId != ActorId) return Conflict(new { error = "not_lock_owner" });
        state.LockOwnerId = null;
        state.LockOwnerName = null;
        state.LockCreatedAtUtc = null;
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return Ok(new { released = true });
    }

    [RequireAdmin, AuditAction("about.draft.save")]
    [HttpPut("api/admin/about/draft")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> SaveDraft([FromBody] DraftRequest request, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var state = await GetState(token);
        if (state.LockOwnerId != ActorId) return Conflict(new { error = "lock_required" });
        if (state.DraftRevision != request.ExpectedRevision) return Conflict(new { error = "revision_conflict", revision = state.DraftRevision });
        var normalized = AboutDocumentValidator.Normalize(request.Document);
        if (normalized.Error is not null) return BadRequest(new { error = normalized.Error });
        state.DraftJson = normalized.Json!;
        state.DraftRevision++;
        state.DraftUpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return Ok(new { revision = state.DraftRevision, document = state.DraftJson, normalized = normalized.Changed });
    }

    [RequireAdmin, AuditAction("about.publish")]
    [HttpPost("api/admin/about/publish")]
    public async Task<IActionResult> Publish([FromBody] RevisionRequest request, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var state = await GetState(token);
        if (state.LockOwnerId != ActorId) return Conflict(new { error = "lock_required" });
        if (state.DraftRevision != request.ExpectedRevision) return Conflict(new { error = "revision_conflict", revision = state.DraftRevision });
        var normalized = AboutDocumentValidator.Normalize(state.DraftJson);
        if (normalized.Error is not null) return BadRequest(new { error = normalized.Error });
        var doc = JsonNode.Parse(normalized.Json!)!;
        if (doc["sections"] is not JsonArray sections || sections.Count == 0 || !HasPublishableContent(sections))
            return BadRequest(new { error = "empty_document" });
        var number = (await db.AboutPageVersions.MaxAsync(x => (long?)x.VersionNumber, token) ?? 0) + 1;
        var version = new AboutPageVersion { VersionNumber = number, DocumentJson = normalized.Json!, PublisherId = ActorId,
            PublisherName = User.Identity?.Name ?? "管理员", PublishedAtUtc = DateTimeOffset.UtcNow, SourceDraftRevision = state.DraftRevision };
        db.AboutPageVersions.Add(version);
        state.CurrentVersionId = version.Id;
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return Ok(new { version.Id, version.VersionNumber, version.PublishedAtUtc });
    }

    [RequireAdmin]
    [HttpGet("api/admin/about/versions")]
    public async Task<IActionResult> Versions([FromQuery] int skip = 0, [FromQuery] int take = 20, CancellationToken token = default)
    {
        take = Math.Clamp(take, 1, 100); skip = Math.Max(skip, 0);
        var query = db.AboutPageVersions.AsNoTracking().OrderByDescending(x => x.VersionNumber);
        return Ok(new { total = await query.CountAsync(token), items = await query.Skip(skip).Take(take)
            .Select(x => new { x.Id, x.VersionNumber, x.PublisherId, x.PublisherName, x.PublishedAtUtc, x.SourceDraftRevision }).ToListAsync(token) });
    }

    [RequireAdmin]
    [HttpGet("api/admin/about/versions/{id:guid}")]
    public async Task<IActionResult> Version(Guid id, CancellationToken token)
    {
        var version = await db.AboutPageVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        if (version is null) return NotFound();
        var normalized = AboutDocumentValidator.Normalize(version.DocumentJson);
        return normalized.Error is not null ? Conflict(new { error = normalized.Error }) : Ok(version);
    }

    [RequireAdmin, AuditAction("about.version.restore")]
    [HttpPost("api/admin/about/versions/{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, [FromBody] RevisionRequest request, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var state = await GetState(token);
        if (state.LockOwnerId != ActorId) return Conflict(new { error = "lock_required" });
        if (state.DraftRevision != request.ExpectedRevision) return Conflict(new { error = "revision_conflict", revision = state.DraftRevision });
        var version = await db.AboutPageVersions.SingleOrDefaultAsync(x => x.Id == id, token);
        if (version is null) return NotFound();
        if (AboutDocumentValidator.Normalize(version.DocumentJson).Error is not null) return Conflict(new { error = "unsupported_version_schema" });
        state.DraftJson = version.DocumentJson;
        state.DraftRevision++;
        state.DraftUpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return Ok(new { revision = state.DraftRevision, sourceVersion = version.VersionNumber });
    }

    [RequireAdmin, AuditAction("about.image.upload")]
    [HttpPost("api/admin/about/images")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken token)
    {
        var state = await GetState(token);
        if (state.LockOwnerId != ActorId) return Conflict(new { error = "lock_required" });
        if (file.Length is <= 0 or > 10 * 1024 * 1024) return BadRequest(new { error = "image_size_invalid" });
        var stored = await blobs.CreateOrUpdateImage(file, "about-page", 0, token);
        if (stored is null) return BadRequest(new { error = "invalid_image" });
        return Ok(new { url = $"/assets/{stored.Hash}/{Uri.EscapeDataString(stored.Name)}", stored.Name, stored.Hash });
    }

    private Guid ActorId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private Task<AboutPageState> GetState(CancellationToken token) => db.AboutPageStates.SingleAsync(x => x.Id == 1, token);

    private static bool HasPublishableContent(JsonArray sections)
    {
        return sections.OfType<JsonObject>()
            .SelectMany(section => section["items"] is JsonArray items ? items.OfType<JsonObject>() : [])
            .Any(item => item["type"]?.ToString() switch
            {
                "text" => !string.IsNullOrWhiteSpace(item["title"]?.ToString()) ||
                          !string.IsNullOrWhiteSpace(new HtmlParser().ParseDocument(item["html"]?.ToString() ?? string.Empty).Body?.TextContent),
                "image" => !string.IsNullOrWhiteSpace(item["url"]?.ToString()),
                "timeline" => item["events"] is JsonArray { Count: > 0 },
                _ => false
            });
    }
}

public sealed record DraftRequest(string Document, long ExpectedRevision);
public sealed record RevisionRequest(long ExpectedRevision);

public static class AboutDocumentValidator
{
    private static readonly HashSet<string> PresetClasses = new(StringComparer.Ordinal)
        { "about-align-left", "about-align-center", "about-align-right", "about-align-justify", "about-color-cyan", "about-color-blue", "about-color-green", "about-color-orange", "about-color-red", "about-color-purple" };
    private static readonly Dictionary<string, string> PresetColors = new(StringComparer.OrdinalIgnoreCase)
        { ["#22d3ee"] = "about-color-cyan", ["#60a5fa"] = "about-color-blue", ["#34d399"] = "about-color-green", ["#fb923c"] = "about-color-orange", ["#f87171"] = "about-color-red", ["#c084fc"] = "about-color-purple" };
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
        { "p", "br", "h1", "h2", "h3", "h4", "strong", "b", "em", "i", "u", "s", "ul", "ol", "li", "blockquote", "a", "table", "thead", "tbody", "tr", "th", "td", "span", "div", "hr", "pre", "code", "sub", "sup", "del" };
    private static readonly HashSet<string> DangerousTags = new(StringComparer.OrdinalIgnoreCase)
        { "script", "style", "iframe", "object", "embed", "template", "svg", "math", "form", "input", "button" };

    public static (string? Json, string? Error, bool Changed) Normalize(string raw)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(raw) > 2 * 1024 * 1024) return (null, "document_too_large", false);
        try
        {
            if (JsonNode.Parse(raw) is not JsonObject root) return (null, "invalid_document", false);
            if (root.Any(pair => pair.Key is not ("schemaVersion" or "title" or "sections"))) return (null, "unknown_document_field", false);
            if (root["schemaVersion"]?.GetValue<int>() != 1) return (null, "unsupported_schema_version", false);
            if (root["title"] is not JsonValue title || string.IsNullOrWhiteSpace(title.ToString()) || title.ToString().Length > 120)
                return (null, "invalid_title", false);
            if (root["sections"] is not JsonArray sections || sections.Count > 30) return (null, "invalid_sections", false);
            var componentCount = 0;
            foreach (var section in sections)
            {
                if (section is not JsonObject obj || obj["items"] is not JsonArray items || items.Count > 200) return (null, "invalid_section", false);
                if (obj.Any(pair => pair.Key is not ("id" or "title" or "items"))) return (null, "unknown_section_field", false);
                    if (!HasText(obj["id"], 80) || !HasText(obj["title"], 120)) return (null, "invalid_section_title", false);
                foreach (var node in items)
                {
                    if (++componentCount > 200 || node is not JsonObject item) return (null, "too_many_components", false);
                    if (item["type"]?.ToString() is not ("text" or "image" or "timeline")) return (null, "invalid_component_type", false);
                    var itemType = item["type"]!.ToString();
                    var allowedFields = itemType switch
                    {
                        "text" => new[] { "id", "type", "width", "title", "html" },
                        "image" => new[] { "id", "type", "width", "url", "href", "alt", "caption" },
                        _ => new[] { "id", "type", "width", "title", "events" }
                    };
                    if (item.Any(pair => !allowedFields.Contains(pair.Key, StringComparer.Ordinal))) return (null, "unknown_component_field", false);
                    if (!HasText(item["id"], 80)) return (null, "invalid_component_id", false);
                    if (item["width"]?.GetValue<int>() is not (3 or 4 or 6 or 8 or 12)) return (null, "invalid_component_width", false);
                    if (itemType is "text" or "timeline" && !HasText(item["title"], 160)) return (null, "invalid_component_title", false);
                    if (itemType == "text" && !IsOptionalText(item["html"], 500_000)) return (null, "html_content_too_large", false);
                    if (itemType == "image" && (item["alt"]?.ToString().Length > 500 || item["caption"]?.ToString().Length > 500)) return (null, "invalid_image_text", false);
                    if (itemType == "image")
                    {
                        if (!IsOptionalText(item["url"], 2048) || !IsOptionalText(item["href"], 2048) || !IsOptionalText(item["alt"], 500) || !IsOptionalText(item["caption"], 500))
                            return (null, "invalid_image_field", false);
                        var imageSource = item["url"]?.ToString();
                        if (!string.IsNullOrEmpty(imageSource) && !IsAllowedImageUrl(imageSource)) return (null, "invalid_image_url", false);
                    }
                    if (itemType == "image" && item["href"] is JsonValue imageHref && imageHref.TryGetValue<string>(out var href) &&
                        href.Length > 0 && !(Uri.TryCreate(href, UriKind.Absolute, out var link) && link.Scheme is "http" or "https"))
                        return (null, "invalid_image_link", false);
                    if (itemType == "timeline")
                    {
                        if (item["events"] is not JsonArray events || events.Count > 100) return (null, "too_many_timeline_events", false);
                        foreach (var timelineEvent in events)
                            if (timelineEvent is not JsonObject eventObject || eventObject.Any(pair => pair.Key is not ("time" or "title" or "description" or "image")) ||
                                !HasText(eventObject["time"], 100) || !HasText(eventObject["title"], 160) || eventObject["description"]?.ToString().Length > 100_000 ||
                                !IsOptionalText(eventObject["description"], 100_000) || !IsOptionalText(eventObject["image"], 2048) ||
                                (eventObject["image"]?.ToString() is { Length: > 0 } eventImage && !IsAllowedImageUrl(eventImage)))
                                return (null, "invalid_timeline_event", false);
                    }
                }
            }
            var before = root.ToJsonString();
            CleanNode(root);
            var json = root.ToJsonString();
            return (json, null, json != before);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NullReferenceException or FormatException or ArgumentException)
        {
            return (null, "invalid_document", false);
        }
    }

    private static void CleanNode(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var pair in obj.ToArray())
            {
                if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text) && pair.Key is "html" or "description")
                    obj[pair.Key] = SanitizeHtml(text);
                else CleanNode(pair.Value);
            }
        else if (node is JsonArray array) foreach (var child in array) CleanNode(child);
    }

    private static bool HasText(JsonNode? value, int maxLength) => value is JsonValue textValue && textValue.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) && text.Length <= maxLength;
    private static bool IsOptionalText(JsonNode? value, int maxLength) => value is null || value is JsonValue textValue && textValue.TryGetValue<string>(out var text) && text.Length <= maxLength;
    private static bool IsAllowedImageUrl(string value) => Regex.IsMatch(value, "^/assets/[0-9a-f]{64}/[^?#]+$") ||
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    private static string SanitizeHtml(string html)
    {
        var parser = new HtmlParser();
        var document = parser.ParseDocument($"<!doctype html><html><body>{html}</body></html>");
        var body = document.Body;
        if (body is null) return string.Empty;
        foreach (var element in body.QuerySelectorAll("*").ToArray())
        {
            if (DangerousTags.Contains(element.LocalName))
            {
                element.Remove();
                continue;
            }
            if (!AllowedTags.Contains(element.LocalName))
            {
                var parent = element.Parent;
                if (parent is null) continue;
                foreach (var child in element.ChildNodes.ToArray()) parent.InsertBefore(child, element);
                element.Remove();
                continue;
            }

            if (element.GetAttribute("style") is { } style) ApplyPresetStyles(element, style);
            foreach (var attribute in element.Attributes.ToArray())
            {
                if (attribute.Name.Equals("title", StringComparison.OrdinalIgnoreCase)) continue;
                if (attribute.Name.Equals("class", StringComparison.OrdinalIgnoreCase) &&
                    attribute.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(PresetClasses.Contains)) continue;
                if (attribute.Name.Equals("style", StringComparison.OrdinalIgnoreCase))
                {
                    element.RemoveAttribute(attribute.Name);
                    continue;
                }
                if (element.LocalName.Equals("a", StringComparison.OrdinalIgnoreCase) &&
                    attribute.Name.Equals("href", StringComparison.OrdinalIgnoreCase) &&
                    Uri.TryCreate(attribute.Value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                    continue;
                element.RemoveAttribute(attribute.Name);
            }
        }
        return body.InnerHtml;
    }

    private static void ApplyPresetStyles(IElement element, string style)
    {
        foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = declaration.IndexOf(':');
            if (separator < 0) continue;
            var property = declaration[..separator].Trim();
            var value = declaration[(separator + 1)..].Trim();
            var className = property.Equals("text-align", StringComparison.OrdinalIgnoreCase) && value.ToLowerInvariant() is "left" or "center" or "right" or "justify"
                ? $"about-align-{value.ToLowerInvariant()}"
                : property.Equals("color", StringComparison.OrdinalIgnoreCase) && PresetColors.TryGetValue(value, out var presetColor)
                    ? presetColor
                    : null;
            if (className is null) continue;
            var classes = element.GetAttribute("class")?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal) ?? [];
            classes.Add(className);
            element.SetAttribute("class", string.Join(' ', classes));
        }
    }
}
