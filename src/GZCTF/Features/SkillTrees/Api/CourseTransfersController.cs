using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GZCTF.Features.Auditing.Application;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.LearningPaths.Domain;
using GZCTF.Features.Shared;
using GZCTF.Features.SkillTrees.Application;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Middlewares;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.SkillTrees.Api;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CourseTransferContent(string Kind, Guid ContentId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CourseTransferCommand(Guid OperationId, string Mode, Guid SourceTreeId, string SourceVersion,
    Guid SourceCategoryId, Guid SourceSkillId, Guid TargetTreeId, string TargetVersion, Guid TargetCategoryId,
    Guid TargetSkillId, List<CourseTransferContent> Contents);
public sealed record CourseTransferResponse(CourseWorkspaceResponse Source, CourseWorkspaceResponse Target,
    int AddedCount, int RemovedCount, int DuplicateCount, bool NoOp);

[RequireAdmin, ApiController, Route("api/admin/course-transfers")]
public sealed class CourseTransfersController(AppDbContext db, CourseWorkspaceContentService contentService) : ControllerBase
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [HttpPost, AuditAction("course_workspace.transfer")]
    public async Task<IActionResult> Transfer(CourseTransferCommand command, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Unauthorized();
        if (command.OperationId == Guid.Empty || command.Mode is not ("move" or "reference") ||
            command.Contents is not { Count: > 0 and <= 100 } || command.Contents.Any(c => c is null || c.ContentId == Guid.Empty || c.Kind is not ("lesson" or "challenge")) ||
            !Guid.TryParse(command.SourceVersion, out var sourceVersion) || !Guid.TryParse(command.TargetVersion, out var targetVersion)) return BadRequest();
        var audit = new CourseTransferAuditContext(command.OperationId, command.Mode,
            new(command.SourceTreeId, command.SourceCategoryId, command.SourceSkillId), new(command.TargetTreeId, command.TargetCategoryId, command.TargetSkillId),
            command.Contents.Select(c => new CourseTransferAuditContent(c.Kind, c.ContentId)).ToArray());
        HttpContext.Items[CourseTransferAuditContext.ItemKey] = audit;
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command, Options)));
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        // Serialize retries of an operation even when they refer to unrelated trees.
        var operationLock = BitConverter.ToInt64(SHA256.HashData(actor.ToByteArray().Concat(command.OperationId.ToByteArray()).ToArray()));
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({operationLock})", token);
        var previous = await db.CourseTransferOperations.AsNoTracking().SingleOrDefaultAsync(o => o.ActorId == actor && o.OperationId == command.OperationId, token);
        if (previous is not null)
        {
            if (previous.RequestHash != hash) return ConflictResult("course_transfer_operation_conflict");
            var replay = JsonSerializer.Deserialize<CourseTransferResponse>(previous.ResponseJson, Options)!;
            CourseTransferAuditPath ReplayPath(CourseTransferAuditPath path, CourseDocument doc) => path with {
                TreeName = doc.Name, CategoryName = doc.Categories.FirstOrDefault(c => c.CategoryId == path.CategoryId)?.Name,
                SkillName = doc.Categories.FirstOrDefault(c => c.CategoryId == path.CategoryId)?.Skills.FirstOrDefault(s => s.SkillId == path.SkillId)?.Name };
            HttpContext.Items[CourseTransferAuditContext.ItemKey] = audit with { Source = ReplayPath(audit.Source, replay.Source.Document), Target = ReplayPath(audit.Target, replay.Target.Document) };
            return Content(previous.ResponseJson, "application/json");
        }
        // Every transfer, including opposing cross-tree moves, takes workspace locks in UUID order.
        foreach (var id in new[] { command.SourceTreeId, command.TargetTreeId }.Distinct().Order())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"SkillTreeId\" FROM \"CourseAuthoringWorkspaces\" WHERE \"SkillTreeId\" = {id} FOR UPDATE", token);
        var workspaces = await db.CourseAuthoringWorkspaces.Include(w => w.SkillTree)
            .Where(w => (w.SkillTreeId == command.SourceTreeId || w.SkillTreeId == command.TargetTreeId) && w.SkillTree.DeletedAtUtc == null).ToListAsync(token);
        var source = workspaces.SingleOrDefault(w => w.SkillTreeId == command.SourceTreeId);
        var target = workspaces.SingleOrDefault(w => w.SkillTreeId == command.TargetTreeId);
        if (source is null || target is null) return NotFound();
        if (source.Version != sourceVersion || target.Version != targetVersion) return ConflictResult();
        var sourceDoc = JsonSerializer.Deserialize<CourseDocument>(source.DocumentJson, Options)!;
        var targetDoc = ReferenceEquals(source, target) ? sourceDoc : JsonSerializer.Deserialize<CourseDocument>(target.DocumentJson, Options)!;
        if (!await ValidDocument(sourceDoc, token) || !await ValidDocument(targetDoc, token)) return BadRequest();
        var sourceSkill = sourceDoc.Categories.SingleOrDefault(c => c.CategoryId == command.SourceCategoryId)?.Skills.SingleOrDefault(s => s.SkillId == command.SourceSkillId);
        var targetSkill = targetDoc.Categories.SingleOrDefault(c => c.CategoryId == command.TargetCategoryId)?.Skills.SingleOrDefault(s => s.SkillId == command.TargetSkillId);
        if (sourceSkill is null || targetSkill is null) return BadRequest();
        HttpContext.Items[CourseTransferAuditContext.ItemKey] = audit with {
            Source = audit.Source with { TreeName = sourceDoc.Name, CategoryName = sourceDoc.Categories.Single(c => c.CategoryId == command.SourceCategoryId).Name, SkillName = sourceSkill.Name },
            Target = audit.Target with { TreeName = targetDoc.Name, CategoryName = targetDoc.Categories.Single(c => c.CategoryId == command.TargetCategoryId).Name, SkillName = targetSkill.Name }
        };
        var selectedKeys = command.Contents.Select(c => (c.Kind, c.ContentId)).ToHashSet();
        var selected = sourceSkill.Contents.Where(c => selectedKeys.Contains((c.Kind, c.ContentId))).ToList();
        if (selected.Count != selectedKeys.Count) return BadRequest();
        var self = ReferenceEquals(source, target) && sourceSkill.SkillId == targetSkill.SkillId;
        var duplicates = selected.Count(c => targetSkill.Contents.Any(t => t.Kind == c.Kind && t.ContentId == c.ContentId));
        var added = self ? 0 : selected.Count - duplicates;
        var removed = !self && command.Mode == "move" ? selected.Count : 0;
        if (!self)
        {
            var targetContents = targetSkill.Contents.Concat(selected.Where(c => !targetSkill.Contents.Any(t => t.Kind == c.Kind && t.ContentId == c.ContentId))).ToList();
            targetDoc = Replace(targetDoc, targetSkill.SkillId, targetContents);
            if (ReferenceEquals(source, target)) sourceDoc = targetDoc;
            if (removed > 0) sourceDoc = Replace(sourceDoc, sourceSkill.SkillId, sourceSkill.Contents.Where(c => !selectedKeys.Contains((c.Kind, c.ContentId))).ToList());
            if (ReferenceEquals(source, target)) targetDoc = sourceDoc;
            if (!await ValidDocument(sourceDoc, token) || !await ValidDocument(targetDoc, token)) return BadRequest();
            // Freeze the source authoring payload before removing its reference. Target overrides take precedence.
            if (!ReferenceEquals(source, target))
            {
                try
                {
                    List<string> copiedPayloads = [];
                    foreach (var item in selected.Where(c => !targetSkill.Contents.Any(t => t.Kind == c.Kind && t.ContentId == c.ContentId)))
                        copiedPayloads.Add(await contentService.CopyForTransferAsync(source.SkillTreeId, target.SkillTreeId, item.ContentId, item.Kind, token));
                    // StoreAsync uses a separate transaction. Finish every copy before taking outer reference locks;
                    // otherwise two contents sharing an image would wait on this transaction's own asset lock.
                    await contentService.LockTransferredAssetsAsync(copiedPayloads, token);
                }
                catch (Exception error) when (error is CourseAssetUnavailableException or FileNotFoundException or DirectoryNotFoundException)
                {
                    return BadRequest(ApiError.Validation("course_transfer_asset_unavailable", "A referenced private file is missing. Restore the file before transferring this content.", HttpContext.TraceIdentifier, null));
                }
            }
            if (removed > 0 || (ReferenceEquals(source, target) && added > 0)) { source.DocumentJson = JsonSerializer.Serialize(sourceDoc, Options); source.Version = Guid.NewGuid(); }
            if (added > 0 && !ReferenceEquals(source, target)) { target.DocumentJson = JsonSerializer.Serialize(targetDoc, Options); target.Version = Guid.NewGuid(); }
        }
        var response = new CourseTransferResponse(Response(source), Response(target), added, removed, self ? 0 : duplicates, added == 0 && removed == 0);
        var responseJson = JsonSerializer.Serialize(response, Options);
        db.CourseTransferOperations.Add(new CourseTransferOperation { ActorId = actor, OperationId = command.OperationId, RequestHash = hash, ResponseJson = responseJson });
        try { await db.SaveChangesAsync(token); await transaction.CommitAsync(token); }
        catch (DbUpdateConcurrencyException) { return ConflictResult(); }
        return Content(responseJson, "application/json");
    }
    private static CourseDocument Replace(CourseDocument doc, Guid skillId, List<CourseContent> contents) => doc with {
        Categories = doc.Categories.Select(c => c with { Skills = c.Skills.Select(s => s.SkillId == skillId ? s with { Contents = contents } : s).ToList() }).ToList()
    };
    private static CourseWorkspaceResponse Response(CourseAuthoringWorkspace w) => new(w.SkillTreeId, w.Version.ToString(), JsonSerializer.Deserialize<CourseDocument>(w.DocumentJson, Options)!, w.SkillTree.CurrentPublishedRevisionId != null);
    private IActionResult ConflictResult(string code = SkillTreeProblemCodes.RevisionConflict) => Conflict(ApiError.Conflict(code, "The course workspace or operation changed. Reload and try again.", HttpContext.TraceIdentifier));
    private async Task<bool> ValidDocument(CourseDocument doc, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(doc.Name) || doc.Name.Length > 128 || doc.Summary is null || doc.Summary.Length > 1024 || !SkillTreeIconCatalog.IsSupported(doc.IconKey) || doc.Categories is null || doc.Categories.Count > 100 ||
            doc.Categories.Any(c => c is null || c.CategoryId == Guid.Empty || string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 128 || c.Summary is null || c.Summary.Length > 1024 || !SkillTreeIconCatalog.IsSupported(c.IconKey) || c.Skills is null) ||
            doc.Categories.Select(c => c.CategoryId).Distinct().Count() != doc.Categories.Count) return false;
        var skills = doc.Categories.SelectMany(c => c.Skills).ToArray();
        if (skills.Length > 1000 || skills.Any(s => s is null || s.SkillId == Guid.Empty || string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 128 || s.Summary is null || s.Summary.Length > 1024 || !SkillTreeIconCatalog.IsSupported(s.IconKey) || s.Contents is null) ||
            doc.Categories.Any(c => c.Skills.Select(s => s.SkillId).Distinct().Count() != c.Skills.Count) || skills.GroupBy(s => s.SkillId).Any(g => g.Select(s => JsonSerializer.Serialize(s, Options)).Distinct().Count() > 1)) return false;
        var contents = skills.SelectMany(s => s.Contents).ToArray();
        if (contents.Length > 5000 || contents.Any(c => c is null || c.ContentId == Guid.Empty || c.Kind is not ("lesson" or "challenge") || c.Title is null || c.Title.Length > 256 || c.Summary is null || c.Summary.Length > 2048 || c.State?.Length > 32) ||
            skills.Any(s => s.Contents.Select(c => (c.Kind, c.ContentId)).Distinct().Count() != s.Contents.Count)) return false;
        var categories = doc.Categories.Select(c => c.CategoryId).Distinct().ToArray(); var skillIds = skills.Select(s => s.SkillId).Distinct().ToArray();
        var lessonIds = contents.Where(c => c.Kind == "lesson").Select(c => c.ContentId).Distinct().ToArray(); var challengeIds = contents.Where(c => c.Kind == "challenge").Select(c => c.ContentId).Distinct().ToArray();
        return await db.SkillCategories.CountAsync(c => categories.Contains(c.Id) && c.DeletedAtUtc == null && c.MergedIntoCategoryId == null, token) == categories.Length &&
            await db.Skills.CountAsync(s => skillIds.Contains(s.Id) && s.DeletedAtUtc == null, token) == skillIds.Length &&
            await db.Lessons.CountAsync(l => lessonIds.Contains(l.Id) && l.PublicationState != LessonPublicationState.Retired, token) == lessonIds.Length &&
            await db.Challenges.CountAsync(c => challengeIds.Contains(c.Id) && c.PublicationState != ChallengePublicationState.Retired && c.PublicationState != ChallengePublicationState.Merged, token) == challengeIds.Length;
    }
}
