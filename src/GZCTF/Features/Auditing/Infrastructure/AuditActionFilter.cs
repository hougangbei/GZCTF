using System.Reflection;
using GZCTF.Features.Auditing.Application;
using GZCTF.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GZCTF.Features.Auditing.Infrastructure;

public sealed class AuditActionFilter : IAsyncActionFilter, IOrderedFilter
{
    public int Order => int.MinValue;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var annotation = AuditActionCatalog.GetAction(context.ActionDescriptor);
        if (annotation is null)
        {
            await next();
            return;
        }

        var definition = AuditActionCatalog.Get(annotation.Code);
        var targetId = FindTargetId(context, annotation);
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<AuditActionFilter>>();
        var resolver = new AuditTargetResolver(db);
        var targetName = await ResolveTargetNameAsync(resolver, definition.TargetType, targetId, logger);

        var executed = await next();
        var status = GetStatus(executed);
        targetId ??= GetResultId(executed.Result, definition.TargetType);
        targetName ??= await ResolveTargetNameAsync(resolver, definition.TargetType, targetId, logger);
        var affectedCount = GetAffectedCount(context.ActionArguments, executed.Result, definition.TrackAffectedCount);

        if (executed.Exception is not null)
            logger.LogError("Audited action {ActionCode} failed with {ErrorType}", annotation.Code,
                executed.Exception.GetType().Name);

        await context.HttpContext.RequestServices.GetRequiredService<AuditWriter>()
            .WriteAsync(context.HttpContext, definition, status, targetId, targetName, affectedCount,
                CancellationToken.None);
    }

    private static string? FindTargetId(ActionExecutingContext context, AuditActionAttribute annotation)
    {
        if (annotation.TargetIdParameter == "result") return null;
        if (annotation.TargetIdParameter is { } parameter && context.RouteData.Values.TryGetValue(parameter, out var explicitValue))
            return explicitValue?.ToString();
        foreach (var key in new[] { "id", "userid", "challengeId", "treeId", "categoryId", "cohortId", "dashboardId", "tokenId", "writeupId", "hash" })
        {
            if (context.RouteData.Values.TryGetValue(key, out var routeValue) && routeValue is not null)
                return routeValue.ToString();
        }

        return null;
    }

    private static string? GetResultId(IActionResult? result, string targetType)
    {
        var value = (result as ObjectResult)?.Value;
        if (value is Guid id) return id.ToString();
        if (targetType == "post" && value is string text) return text;
        if (value is null) return null;
        var type = value.GetType();
        var idValue = type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(value)
                      ?? type.GetProperty("TokenId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(value);
        if (idValue is not null) return idValue.ToString();
        var info = type.GetProperty("Info", BindingFlags.Public | BindingFlags.Instance)?.GetValue(value);
        return info?.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(info)?.ToString();
    }

    private static async Task<string?> ResolveTargetNameAsync(AuditTargetResolver resolver, string targetType,
        string? targetId, ILogger logger)
    {
        try
        {
            return await resolver.GetNameAsync(targetType, targetId, CancellationToken.None);
        }
        catch (Exception error)
        {
            logger.LogWarning("Could not resolve audit target for {TargetType}; error type {ErrorType}",
                targetType, error.GetType().Name);
            return null;
        }
    }

    private static int GetStatus(ActionExecutedContext executed) => executed.Result switch
    {
        ObjectResult result when result.StatusCode.HasValue => result.StatusCode.Value,
        StatusCodeResult result => result.StatusCode,
        _ when executed.Exception is not null && !executed.ExceptionHandled => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status200OK
    };

    private static int? GetAffectedCount(IDictionary<string, object?> arguments, IActionResult? result, bool trackBatch)
    {
        var resultValue = (result as ObjectResult)?.Value;
        var resultCount = resultValue?.GetType().GetProperty("AffectedCount")?.GetValue(resultValue);
        if (resultCount is int affectedCount) return affectedCount;
        if (!trackBatch) return null;

        foreach (var item in arguments.Values)
        {
            if (item is System.Collections.ICollection collection) return collection.Count;
            var countProperty = item?.GetType().GetProperty("Count");
            var count = countProperty?.GetIndexParameters().Length == 0 ? countProperty.GetValue(item) : null;
            if (count is int value) return value;
            if (item is not null)
            {
                foreach (var property in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetIndexParameters().Length != 0) continue;
                    try
                    {
                        if (property.GetValue(item) is System.Collections.ICollection nestedCollection)
                            return nestedCollection.Count;
                    }
                    catch (TargetInvocationException)
                    {
                        // A batch DTO may expose an unrelated computed property; ignore it.
                    }
                }
            }
        }
        return null;
    }
}
