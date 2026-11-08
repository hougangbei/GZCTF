using System.Net.Mail;
using GZCTF.Features.Imports.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.Imports.Application;

public sealed class ImportedAccountService(AppDbContext db, UserManager<UserInfo> users)
{
    public async Task<IReadOnlyList<PlatformImportIssue>> PreviewAsync(PlatformImportPackage package,
        PlatformImportOptions options, CancellationToken cancellationToken = default)
    {
        List<PlatformImportIssue> issues = [];
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in package.Users)
            if (!sourceIds.Add(source.SourceId))
                issues.Add(new("error", "duplicate_source_user", source.SourceId, "Source user IDs must be unique."));
        var skippedIds = new HashSet<string>(options.SkipUserIds ?? [], StringComparer.Ordinal);
        foreach (var sourceId in skippedIds)
            if (!sourceIds.Contains(sourceId))
                issues.Add(new("error", "invalid_skip_user", sourceId, "A skipped user must exist in the source package."));
        if (!options.IncludeAccounts)
            return issues;
        var maps = await db.PlatformImportMaps.AsNoTracking()
            .Where(m => m.SourceInstance == options.SourceInstance && m.Kind == "user")
            .ToDictionaryAsync(m => m.SourceId, m => m.TargetId, cancellationToken);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var emails = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in package.Users)
        {
            if (skippedIds.Contains(source.SourceId))
            {
                issues.Add(new("warning", "skipped_source_user", source.SourceId,
                    "This source account was explicitly skipped. Its history remains unlinked to a local account."));
                continue;
            }
            if (maps.TryGetValue(source.SourceId, out var targetId))
            {
                if (!await db.Users.AnyAsync(u => u.Id == targetId, cancellationToken))
                    issues.Add(new("error", "missing_mapped_user", source.SourceId, "The previously imported account no longer exists."));
                continue; // Idempotency never changes an existing user's credentials, email, or role.
            }
            var name = GetUserName(source, options);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 16)
                issues.Add(new("error", "invalid_user_name", source.SourceId,
                    "A username of 1–16 characters is required; supply a username override."));
            else
            {
                var normalized = users.NormalizeName(name);
                if (!names.Add(normalized) || await db.Users.AnyAsync(u => u.NormalizedUserName == normalized, cancellationToken))
                    issues.Add(new("error", "duplicate_user_name", source.SourceId,
                        "The username is already in use; supply a different username override."));
            }
            var email = source.Email?.Trim();
            if (string.IsNullOrWhiteSpace(email) || email.Length > 256 || !MailAddress.TryCreate(email, out var address) || address.Address != email)
                issues.Add(new("error", "invalid_user_email", source.SourceId, "A valid unique email address is required to import this account."));
            else
            {
                var normalized = users.NormalizeEmail(email);
                if (!emails.Add(normalized) || await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken))
                    issues.Add(new("error", "duplicate_user_email", source.SourceId,
                        "The email address is already in use; accounts are never automatically merged."));
            }
            if (!CtfdPasswordHasher.IsSupported(source.PasswordHash))
                issues.Add(new("warning", "password_reset_required", source.SourceId,
                    "The source password format is unsupported or missing. This account must reset its password before signing in."));
        }
        return issues;
    }

    // Called inside the importer transaction. UserManager saves through the same scoped AppDbContext.
    public async Task<int> ImportAsync(PlatformImportPackage package, PlatformImportOptions options,
        PlatformImportBatch batch, CancellationToken cancellationToken = default)
    {
        if (!options.IncludeAccounts)
            return 0;
        var issues = await PreviewAsync(package, options, cancellationToken);
        if (issues.Any(issue => issue.Severity == "error"))
            throw new InvalidOperationException("Imported accounts have unresolved validation errors.");
        var existing = await db.PlatformImportMaps.Where(m => m.SourceInstance == options.SourceInstance && m.Kind == "user")
            .Select(m => m.SourceId).ToHashSetAsync(cancellationToken);
        var skippedIds = new HashSet<string>(options.SkipUserIds ?? [], StringComparer.Ordinal);
        var created = 0;
        foreach (var source in package.Users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (skippedIds.Contains(source.SourceId) || existing.Contains(source.SourceId))
                continue;
            var user = new UserInfo
            {
                UserName = GetUserName(source, options),
                Email = source.Email!.Trim(),
                EmailConfirmed = source.Verified,
                Role = source.Banned ? Role.Banned : Role.User,
                ApprovalStatus = RegistrationApprovalStatus.Approved,
                RegisterTimeUtc = source.CreatedAtUtc ?? DateTimeOffset.UtcNow,
                PasswordHash = CtfdPasswordHasher.IsSupported(source.PasswordHash)
                    ? CtfdPasswordHasher.ImportPrefix + source.PasswordHash : null
            };
            var result = await users.CreateAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("An imported account could not be created: " +
                    string.Join(", ", result.Errors.Select(e => e.Code)));
            db.PlatformImportMaps.Add(new PlatformImportMap
            {
                SourceInstance = options.SourceInstance,
                Kind = "user",
                SourceId = source.SourceId,
                TargetId = user.Id,
                BatchId = batch.Id,
                Batch = batch
            });
            existing.Add(source.SourceId);
            created++;
        }
        await db.SaveChangesAsync(cancellationToken);
        return created;
    }

    private static string GetUserName(PlatformImportUser user, PlatformImportOptions options) =>
        options.UserNameOverrides?.TryGetValue(user.SourceId, out var name) == true ? name.Trim() : user.UserName.Trim();
}
