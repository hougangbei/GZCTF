using System.Net.Mime;
using GZCTF.Features.ChallengeLibrary.Application;
using GZCTF.Features.Shared;
using GZCTF.Features.SkillTrees.Application;
using GZCTF.Middlewares;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Features.ChallengeLibrary.Api;

[RequireAdmin]
[ApiController]
[Route("api/admin/lessons")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class AdminLessonsController(
    ChallengeLibraryService service,
    ContentPublicationService publicationService,
    CourseWorkspaceContentService workspaceContent) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LessonResponse>>> List(
        [FromQuery] string? locale, CancellationToken token) =>
        Ok(await service.ListLessonsAsync(locale, token));

    [HttpPost]
    [AuditAction("lessons.create")]
    public async Task<ActionResult<LessonResponse>> Create(
        [FromBody] LessonCommand command, CancellationToken token) =>
        Ok(await service.CreateLessonAsync(command, token));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LessonResponse>> Get(
        Guid id, [FromQuery] string? locale, [FromQuery] Guid? course, CancellationToken token)
    {
        var response = course is { } treeId
            ? await workspaceContent.GetLessonAsync(treeId, id, locale, token)
            : await service.GetLessonAsync(id, locale, token);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpPut("{id:guid}")]
    [AuditAction("lessons.update")]
    public async Task<ActionResult<LessonResponse>> Update(
        Guid id, [FromBody] LessonCommand command, [FromQuery] Guid? course, CancellationToken token)
    {
        try
        {
            var response = course is { } treeId
                ? await workspaceContent.UpdateLessonAsync(treeId, id, command, token)
                : await service.UpdateLessonAsync(id, command, token);
            if (response is null && course is not null)
                return Conflict(ApiError.Conflict("course_content_not_in_workspace", "Content is not in this workspace.", HttpContext.TraceIdentifier));
            return response is null ? NotFound() : Ok(response);
        }
        catch (SkillTreeRevisionConflictException)
        {
            return Conflict(ApiError.Conflict(SkillTreeProblemCodes.RevisionConflict, "The course content changed since it was loaded.", HttpContext.TraceIdentifier));
        }
        catch (CourseAssetUnavailableException)
        {
            return BadRequest(ApiError.Validation("content_asset_unreadable", "A referenced private file is missing.", HttpContext.TraceIdentifier, null));
        }
    }

    [HttpDelete("{id:guid}")]
    [AuditAction("lessons.delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken token) =>
        await service.DeleteLessonAsync(id, token) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/publish")]
    [AuditAction("lessons.publish")]
    public async Task<IActionResult> Publish(
        Guid id, [FromBody] PublishContentCommand command, CancellationToken token)
    {
        try
        {
            await publicationService.PublishLessonAsync(id, command, token);
            return NoContent();
        }
        catch (SkillSelectionRequiredException)
        {
            return Conflict(ApiError.Conflict("skill_selection_required", "Select a skill explicitly for this category.", HttpContext.TraceIdentifier));
        }
        catch (SkillCategoryValidationException exception)
        {
            return BadRequest(ApiError.Validation("skill_invalid", exception.Message, HttpContext.TraceIdentifier, null));
        }
        catch (SkillTreeInvalidIconException)
        {
            return BadRequest(ApiError.Validation("skill_tree_invalid_icon", "The icon is not supported.", HttpContext.TraceIdentifier, null));
        }
        catch (ContentCategoryRequiredException)
        {
            return BadRequest(ApiError.Validation(
                SkillTreeProblemCodes.ContentCategoryRequired,
                "At least one category is required before publication.",
                HttpContext.TraceIdentifier, null));
        }
        catch (ContentCategoryHasNoTreeException)
        {
            return BadRequest(ApiError.Validation(
                SkillTreeProblemCodes.CategoryHasNoTree,
                "The selected category does not belong to any active skill tree.",
                HttpContext.TraceIdentifier, null));
        }
        catch (SkillTreeRevisionConflictException)
        {
            return Conflict(ApiError.Conflict(
                SkillTreeProblemCodes.RevisionConflict,
                "The content was changed by another administrator.",
                HttpContext.TraceIdentifier));
        }
        catch (ContentPublicationValidationException exception)
        {
            return BadRequest(ApiError.Validation(
                "content_invalid_publication", exception.Message,
                HttpContext.TraceIdentifier, null));
        }
    }
}
