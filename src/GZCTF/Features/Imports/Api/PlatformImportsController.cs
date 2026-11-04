using System.Security.Claims;
using System.Text.Json;
using GZCTF.Features.Imports.Application;
using GZCTF.Features.Imports.Infrastructure;
using GZCTF.Middlewares;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.Imports.Api;

[RequireAdmin, ApiController, Route("api/admin/platform-imports")]
public sealed class PlatformImportsController(AppDbContext db, PlatformArchiveReader reader, PlatformImportService service) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken token) => Ok((await db.PlatformImportBatches.AsNoTracking().OrderByDescending(item => item.CreatedAtUtc).Take(100).ToArrayAsync(token)).Select(PlatformImportService.ToInfo));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken token)
    {
        var batch = await db.PlatformImportBatches.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, token);
        return batch is null ? NotFound() : Ok(PlatformImportService.ToInfo(batch));
    }
    [HttpPost("preview"), AuditAction("platform-imports.preview"), RequestSizeLimit(PlatformArchiveReader.MaxCompressedBytes + 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = PlatformArchiveReader.MaxCompressedBytes)]
    public Task<IActionResult> Preview(IFormFile package, [FromForm] string options, CancellationToken token) => Handle(package, options, null, token);
    [HttpPost, AuditAction("platform-imports.execute"), RequestSizeLimit(PlatformArchiveReader.MaxCompressedBytes + 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = PlatformArchiveReader.MaxCompressedBytes)]
    public Task<IActionResult> Execute(IFormFile package, [FromForm] string options, [FromForm] string planFingerprint, CancellationToken token) => Handle(package, options, planFingerprint, token);
    private async Task<IActionResult> Handle(IFormFile package, string options, string? planFingerprint, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            if (package.Length is <= 0 or > PlatformArchiveReader.MaxCompressedBytes || options.Length > 32768)
                return BadRequest(new { code = "import.invalid_package" });
            var settings = JsonSerializer.Deserialize<PlatformImportOptions>(options, JsonOptions) ?? throw new InvalidDataException("import.invalid_options");
            await using var stream = package.OpenReadStream();
            var parsed = await reader.ReadAsync(stream, token);
            if (planFingerprint is null)
                return Ok(await service.PreviewAsync(parsed, settings, token));
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Unauthorized();
            return Ok(await service.ImportAsync(parsed, settings, planFingerprint, userId, token));
        }
        catch (PlatformImportPlanChangedException) { return Conflict(new { code = "import.plan_changed", message = "导入配置或目标已变化，请重新预检。" }); }
        catch (PlatformImportRejectedException exception) { return BadRequest(exception.Preview); }
        catch (InvalidDataException exception) { return BadRequest(new { code = exception.Message, message = "导出包无法读取，请检查格式及文件完整性。" }); }
        catch (JsonException) { return BadRequest(new { code = "import.invalid_json", message = "导出包或导入选项格式不正确。" }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { code = "import.target_changed", message = "目标数据已被修改，请重新预检。" }); }
        catch (DbUpdateException) { return Conflict(new { code = "import.conflict", message = "账号、来源或目标数据发生冲突，请重新预检。" }); }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("import.", StringComparison.Ordinal)) { return Conflict(new { code = exception.Message, message = "来源映射或账号数据已变化，请重新预检。" }); }
    }
}

[ApiController, RequireUser, Route("api/account/import-history")]
public sealed class PlatformHistoryController(PlatformHistoryService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken token = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();
        return Ok(await service.GetAsync(userId, false, skip, take, token));
    }
}
[ApiController, RequireAdmin, Route("api/admin/platform-imports/history")]
public sealed class AdminPlatformHistoryController(PlatformHistoryService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken token = default)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.GetAsync(Guid.Empty, true, skip, take, token));
    }
}
