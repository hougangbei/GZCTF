using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Abstractions;

namespace GZCTF.Features.Auditing.Application;

[AttributeUsage(AttributeTargets.Method)]
public sealed class AuditActionAttribute(string code) : Attribute
{
    public string Code { get; } = code;
    public string? TargetIdParameter { get; set; }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class AuditExcludedAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}

public sealed record AuditActionDefinition(string Code, string Category, string TargetType, bool TrackAffectedCount = false);

public static class AuditActionCatalog
{
    private static readonly IReadOnlyDictionary<string, AuditActionDefinition> Actions =
        new Dictionary<string, AuditActionDefinition>(StringComparer.Ordinal)
        {
            ["users.config.update"] = new("users.config.update", "settings", "configuration"),
            ["users.logo.upload"] = new("users.logo.upload", "settings", "logo"),
            ["users.logo.reset"] = new("users.logo.reset", "settings", "logo"),
            ["users.create_batch"] = new("users.create_batch", "users", "batch", true),
            ["users.update"] = new("users.update", "users", "user"),
            ["users.password.reset"] = new("users.password.reset", "users", "user"),
            ["users.delete"] = new("users.delete", "users", "user"),
            ["users.registration.approve"] = new("users.registration.approve", "users", "user"),
            ["users.registration.reject"] = new("users.registration.reject", "users", "user"),
            ["instances.admin_container_stop"] = new("instances.admin_container_stop", "containers", "container"),
            ["tokens.create"] = new("tokens.create", "settings", "api_token"),
            ["tokens.restore"] = new("tokens.restore", "settings", "api_token"),
            ["tokens.revoke"] = new("tokens.revoke", "settings", "api_token"),
            ["assets.upload"] = new("assets.upload", "content", "asset", true),
            ["assets.delete"] = new("assets.delete", "content", "asset"),
            ["posts.create"] = new("posts.create", "content", "post"),
            ["posts.update"] = new("posts.update", "content", "post"),
            ["posts.delete"] = new("posts.delete", "content", "post"),
            ["challenges.create"] = new("challenges.create", "challenges", "challenge"),
            ["challenges.update"] = new("challenges.update", "challenges", "challenge"),
            ["challenges.retire"] = new("challenges.retire", "challenges", "challenge"),
            ["challenges.publish"] = new("challenges.publish", "challenges", "challenge"),
            ["challenges.merge"] = new("challenges.merge", "challenges", "challenge", true),
            ["lessons.create"] = new("lessons.create", "challenges", "lesson"),
            ["lessons.update"] = new("lessons.update", "challenges", "lesson"),
            ["lessons.delete"] = new("lessons.delete", "challenges", "lesson"),
            ["lessons.publish"] = new("lessons.publish", "challenges", "lesson"),
            ["settings.qq.update"] = new("settings.qq.update", "settings", "qq_settings"),
            ["settings.qq.test"] = new("settings.qq.test", "settings", "qq_test"),
            ["updates.apply"] = new("updates.apply", "settings", "deployment"),
            ["imports.zip"] = new("imports.zip", "content", "import_batch", true),
            ["writeups.review"] = new("writeups.review", "content", "writeup"),
            ["cohorts.create"] = new("cohorts.create", "cohorts", "cohort"),
            ["cohorts.update"] = new("cohorts.update", "cohorts", "cohort"),
            ["cohorts.status"] = new("cohorts.status", "cohorts", "cohort"),
            ["cohorts.members.add"] = new("cohorts.members.add", "cohorts", "cohort_batch", true),
            ["cohorts.members.remove"] = new("cohorts.members.remove", "cohorts", "cohort_member"),
            ["dashboards.create"] = new("dashboards.create", "dashboards", "dashboard"),
            ["dashboards.update"] = new("dashboards.update", "dashboards", "dashboard"),
            ["dashboards.tokens.create"] = new("dashboards.tokens.create", "dashboards", "dashboard_token"),
            ["dashboards.tokens.rotate"] = new("dashboards.tokens.rotate", "dashboards", "dashboard_token"),
            ["dashboards.tokens.revoke"] = new("dashboards.tokens.revoke", "dashboards", "dashboard_token"),
            ["dashboards.rebuild"] = new("dashboards.rebuild", "dashboards", "dashboard_projection"),
            ["skill_trees.create"] = new("skill_trees.create", "skill_trees", "skill_tree"),
            ["skill_trees.draft.get"] = new("skill_trees.draft.get", "skill_trees", "skill_tree"),
            ["skill_trees.draft.update"] = new("skill_trees.draft.update", "skill_trees", "skill_tree"),
            ["skill_trees.publish"] = new("skill_trees.publish", "skill_trees", "skill_tree"),
            ["skill_trees.delete"] = new("skill_trees.delete", "skill_trees", "skill_tree"),
            ["skill_categories.create"] = new("skill_categories.create", "skill_trees", "skill_category"),
            ["skill_categories.update"] = new("skill_categories.update", "skill_trees", "skill_category"),
            ["skill_categories.contents.update"] = new("skill_categories.contents.update", "skill_trees", "skill_category"),
            ["skill_categories.memberships.update"] = new("skill_categories.memberships.update", "skill_trees", "skill_category"),
            ["skill_categories.delete"] = new("skill_categories.delete", "skill_trees", "skill_category"),
            ["skill_categories.merge"] = new("skill_categories.merge", "skill_trees", "skill_category", true),
            ["about.lock.acquire"] = new("about.lock.acquire", "content", "about_page"),
            ["about.lock.release"] = new("about.lock.release", "content", "about_page"),
            ["about.draft.save"] = new("about.draft.save", "content", "about_page"),
            ["about.publish"] = new("about.publish", "content", "about_page_version"),
            ["about.version.restore"] = new("about.version.restore", "content", "about_page_version"),
            ["about.image.upload"] = new("about.image.upload", "content", "asset"),
            ["about.lock.emergency_unlock"] = new("about.lock.emergency_unlock", "content", "about_page"),
            ["challenge_instances.settings.update"] = new("challenge_instances.settings.update", "containers", "challenge_instance_settings"),
            ["challenge_instances.admin_stop"] = new("challenge_instances.admin_stop", "containers", "challenge_instance"),
            ["challenge_instances.start"] = new("challenge_instances.start", "containers", "challenge"),
            ["challenge_instances.extend"] = new("challenge_instances.extend", "containers", "challenge"),
            ["challenge_instances.stop"] = new("challenge_instances.stop", "containers", "challenge"),
        };

    public static IReadOnlyCollection<AuditActionDefinition> All => Actions.Values.ToArray();

    public static AuditActionDefinition Get(string code) => Actions.TryGetValue(code, out var action)
        ? action
        : throw new InvalidOperationException($"Audit action '{code}' is not registered.");

    public static AuditActionAttribute? GetAction(ActionDescriptor action) =>
        action.EndpointMetadata?.OfType<AuditActionAttribute>().FirstOrDefault()
        ?? (action as Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor)?.MethodInfo
            .GetCustomAttributes(typeof(AuditActionAttribute), true).OfType<AuditActionAttribute>().FirstOrDefault();
}
