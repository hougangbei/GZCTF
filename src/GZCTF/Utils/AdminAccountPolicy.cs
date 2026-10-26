using GZCTF.Models.Data;
using GZCTF.Models.Request.Admin;

namespace GZCTF.Utils;

/// <summary>The seeded Admin account is the only administrator delegator.</summary>
public static class AdminAccountPolicy
{
    public static bool IsReservedName(string? name) =>
        string.Equals(name, "Admin", StringComparison.OrdinalIgnoreCase);

    public static bool IsOwner(UserInfo? user) => user?.Role == Role.Admin && IsReservedName(user.UserName);

    public static bool CanManage(UserInfo? actor, UserInfo target) =>
        actor is not null && (IsOwner(actor) || target.Role < Role.Admin);

    public static bool CanEdit(UserInfo? actor, UserInfo target, AdminUserInfoModel update)
    {
        if (update.Role is { } requested && !Enum.IsDefined(requested)) return false;
        if (IsReservedName(update.UserName) && !IsOwner(target)) return false;
        // Keeping the reserved name and role avoids losing the only delegating identity.
        if (IsOwner(target) &&
            (update.UserName is { } name && !IsReservedName(name) ||
             update.Role is { } ownerRole && ownerRole != Role.Admin)) return false;
        if (IsOwner(actor)) return true;
        if (actor is null || target.Role >= Role.Admin && actor.Id != target.Id) return false;
        return update.Role is not { } role || role == target.Role ||
               role < Role.Admin && target.Role < Role.Admin;
    }
}
