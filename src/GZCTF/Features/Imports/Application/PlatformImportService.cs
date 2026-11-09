using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.Imports.Domain;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Features.SkillTrees.Application;
using GZCTF.Storage.Interface;
using GZCTF.Utils;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;
using CanonicalChallenge = GZCTF.Features.ChallengeLibrary.Domain.Challenge;

namespace GZCTF.Features.Imports.Application;

public sealed class PlatformImportRejectedException(PlatformImportPreview preview) : Exception("import.preflight_failed")
{
    public PlatformImportPreview Preview { get; } = preview;
}
public sealed class PlatformImportPlanChangedException : Exception { }
public sealed record PlatformImportBatchInfo(Guid Id, string SourceInstance, string Format, string Title,
    DateTimeOffset CreatedAtUtc, Guid? TreeId, int ChallengeCount, int UserCount, int TeamCount,
    int SubmissionCount, int AwardCount, string ReportJson);

public sealed partial class PlatformImportService(AppDbContext db, ImportedAccountService accounts,
    PlatformHistoryService history, IBlobStorage storage, ISkillTreeCacheInvalidator cache, ILogger<PlatformImportService>? logger = null,
    Microsoft.Extensions.Caching.Memory.IMemoryCache? memoryCache = null, PlatformScoreMigrationService? scoreMigration = null)
{
    public async Task<PlatformImportPreview> PreviewAsync(PlatformImportPackage package, PlatformImportOptions options,
        CancellationToken token = default)
    {
        List<PlatformImportIssue> issues = [.. package.Issues.Select(issue => options.MergeScores && issue.Code == "historical_score_unavailable"
            ? issue with { Message = "逐条历史得分缺失的记录仍保留为空；初始总分独立按来源题目快照及奖励计算。" }
            : issue)];
        if (!SourceName().IsMatch(options.SourceInstance ?? ""))
            issues.Add(new("error", "invalid_source_instance", "", "来源标识需为1–128位字母、数字、点、下划线、冒号、斜线或横线。"));
        if ((options.TreeName ?? package.Title).Length is < 1 or > 128)
            issues.Add(new("error", "invalid_tree_name", "", "路线名称需为1–128个字符。"));
        if (await db.PlatformImportBatches.AnyAsync(item => item.SourceInstance == options.SourceInstance && item.Format != package.Format, token))
            issues.Add(new("error", "source_format_changed", "", "此来源标识已经用于另一种平台，请使用不同来源标识。"));
        var mappings = await db.PlatformImportMaps.AsNoTracking().Where(item => item.SourceInstance == options.SourceInstance)
            .ToArrayAsync(token);
        uint? targetVersion = null;
        if (options.TargetSkillId is { } target)
        {
            var skill = await db.Skills.AsNoTracking().SingleOrDefaultAsync(item => item.Id == target && item.DeletedAtUtc == null, token);
            if (skill is null)
                issues.Add(new("error", "target_skill_missing", "", "目标技能不存在或已删除。"));
            else
            {
                targetVersion = skill.RowVersion;
                if (options.TargetSkillRowVersion != skill.RowVersion)
                    issues.Add(new("error", "target_skill_changed", "", "目标技能已经修改，请刷新后重新预检。"));
            }
        }
        foreach (var identifier in package.Challenges.Select(item => item.SourceId).Concat(package.Users.Select(item => item.SourceId)).Concat(package.Teams.Select(item => item.SourceId)).Concat(package.Submissions.Select(item => item.SourceId)).Concat(package.Awards.Select(item => item.SourceId)))
            if (identifier.Length is < 1 or > 128)
                issues.Add(new("error", "invalid_source_id", "", "来源对象编号需为1–128个字符。"));
        var priorIds = mappings.Where(item => item.Kind == "challenge").Select(item => item.SourceId).ToHashSet(StringComparer.Ordinal);
        foreach (var challenge in package.Challenges)
        {
            if (priorIds.Contains(challenge.SourceId))
                issues.Add(new("warning", "existing_challenge_preserved", challenge.SourceId, "已导入的题目将复用，保留本地编辑，不会覆盖。"));
            if (challenge.Title.Length is < 1 or > 128)
                issues.Add(new("error", "invalid_challenge_title", challenge.SourceId, "题目标题需为1–128个字符。"));
            if (string.IsNullOrWhiteSpace(challenge.Category) || challenge.Category.Length > 128)
                issues.Add(new("error", "invalid_category_name", challenge.SourceId, "类别名称需为1–128个字符。"));
            if (challenge.SubmissionLimit < 0)
                issues.Add(new("error", "invalid_submission_limit", challenge.SourceId, "提交次数限制不能为负数。"));
            foreach (var resource in challenge.Attachments)
                if (resource.FileName.Length is < 1 or > 255 || resource.FileName.Any(char.IsControl))
                    issues.Add(new("error", "invalid_attachment_name", challenge.SourceId, "附件名称需为1–255个字符，不能包含控制字符。"));
            if (challenge.Score is < 0 or > 10000)
                issues.Add(new("error", "score_out_of_range", challenge.SourceId, "训练分数需在0–10000之间，请调整来源分数后重新导出。"));
            foreach (var flag in challenge.Flags)
                if (flag.Value.Length > (flag.Kind == "template" ? Limits.MaxFlagTemplateLength : 512))
                    issues.Add(new("error", "flag_too_long", challenge.SourceId, "Flag或模板长度超出目标平台限制。"));
            foreach (var attachment in challenge.Attachments)
                if (attachment.RemoteUrl is null && (!package.Files.TryGetValue(attachment.ArchivePath, out var bytes) ||
                    !string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), attachment.Sha256, StringComparison.OrdinalIgnoreCase)))
                    issues.Add(new("error", "invalid_attachment", challenge.SourceId, "附件缺失或校验失败。"));
        }
        if (options.TargetSkillId is null)
            foreach (var group in package.Challenges.GroupBy(c => c.Category.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Select(c => c.Category).Distinct(StringComparer.Ordinal).Count() > 1))
                issues.Add(new("warning", "category_casefold_merge", "", $"同包类别 {string.Join(" / ", group.Select(c => c.Category).Distinct(StringComparer.Ordinal))} 共{group.Count()}题，新导入将归入 {group.First().Category.Trim()}；既有来源映射优先保留。"));
        issues.Add(options.MergeScores
            ? new("warning", "source_score_snapshot", "", "原站总分将固定为初始积分；原有解题只恢复进度，后续新解题另行加分。")
            : new("warning", "source_scoring_retained", "", "原比赛计分规则、截止日期和赛制仅保存在来源档案中，不直接套用为训练限制。"));
        issues.AddRange(await accounts.PreviewAsync(package, options, token));
        issues.AddRange(await (scoreMigration ?? new PlatformScoreMigrationService(db)).PreviewAsync(package, options, token));
        if (package.Format == "GZCTF-GAME" && options.IncludeAccounts)
            issues.Add(new("warning", "no_accounts_in_game_export", "", "比赛包不包含账号或历史成绩；本次只导入题目与资源。"));
        var plan = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            package.Fingerprint,
            options.SourceInstance,
            options.TreeName,
            options.TargetSkillId,
            options.TargetSkillRowVersion,
            options.IncludeAccounts,
            options.IncludeHistory,
            options.MergeScores,
            SkippedUsers = options.SkipUserIds?.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Overrides = options.UserNameOverrides?.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray(),
            TargetVersion = targetVersion,
        })));
        return new(package.Format, package.Fingerprint, package.Title, package.Challenges.Count,
            package.Files.Count,
            package.Users.Count, package.Teams.Count, package.Submissions.Count, package.Awards.Count,
            issues, issues.All(item => item.Severity != "error"), plan);
    }

    public async Task<PlatformImportBatchInfo> ImportAsync(PlatformImportPackage package, PlatformImportOptions options,
        string planFingerprint, Guid administratorId, CancellationToken token = default)
    {
        // Serialize all updates from a source, including retries and account/name conflict checks.
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var lockKey = BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes("platform-import:" + options.SourceInstance)));
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", token);
        if (options.IncludeAccounts)
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({-329574250683364781L})", token);
        var existing = await db.PlatformImportBatches.AsNoTracking().SingleOrDefaultAsync(item =>
            item.SourceInstance == options.SourceInstance && item.Fingerprint == package.Fingerprint && item.PlanFingerprint == planFingerprint, token);
        if (existing is not null)
            return ToInfo(existing);
        var preview = await PreviewAsync(package, options, token);
        if (!string.Equals(preview.PlanFingerprint, planFingerprint, StringComparison.Ordinal))
            throw new PlatformImportPlanChangedException();
        if (!preview.CanImport)
            throw new PlatformImportRejectedException(preview);
        if (options.MergeScores)
        {
            var migrated = await db.PlatformScoreMigrations.AsNoTracking().SingleOrDefaultAsync(m => m.SourceInstance == options.SourceInstance, token);
            if (migrated is not null)
                return ToInfo(await db.PlatformImportBatches.AsNoTracking().SingleAsync(b => b.Id == migrated.BatchId, token));
        }
        var batch = new PlatformImportBatch
        {
            SourceInstance = options.SourceInstance,
            Format = package.Format,
            Fingerprint = package.Fingerprint,
            PlanFingerprint = planFingerprint,
            Title = package.Title,
            UserId = administratorId,
            ChallengeCount = package.Challenges.Count,
            TeamCount = options.IncludeHistory ? package.Teams.Count : 0,
            SubmissionCount = options.IncludeHistory ? package.Submissions.Count : 0,
            AwardCount = options.IncludeHistory ? package.Awards.Count : 0,
            ReportJson = JsonSerializer.Serialize(preview.Issues)
        };
        db.PlatformImportBatches.Add(batch);
        var maps = await db.PlatformImportMaps.Where(item => item.SourceInstance == options.SourceInstance)
            .ToDictionaryAsync(item => (item.Kind, item.SourceId), token);
        List<string> staged = [];
        var committed = false;
        try
        {
            foreach (var bytes in package.Files.Values)
            {
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                var key = $"platform-import/{batch.Id:N}/{hash}";
                if (staged.Contains(key, StringComparer.Ordinal))
                    continue;
                await using var stream = new MemoryStream(bytes, writable: false);
                staged.Add(key);
                await storage.WriteAsync(key, stream, cancellationToken: token);
            }
            SkillTree? tree = null;
            SkillTreeRevision? revision = null;
            Skill? targetSkill = null;
            if (options.TargetSkillId is { } target)
            {
                targetSkill = await db.Skills.SingleAsync(item => item.Id == target, token);
                if (targetSkill.RowVersion != options.TargetSkillRowVersion)
                    throw new PlatformImportPlanChangedException();
                targetSkill.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
            else
            {
                var sourceTreeId = TreeSourceId(package);
                if (maps.TryGetValue(("tree", sourceTreeId), out var treeMap))
                {
                    tree = await db.SkillTrees.Include(item => item.Revisions).ThenInclude(item => item.Categories)
                        .SingleOrDefaultAsync(item => item.Id == treeMap.TargetId && item.DeletedAtUtc == null, token)
                        ?? throw new InvalidOperationException("import.mapped_tree_missing");
                    revision = tree.Revisions.SingleOrDefault(item => item.Status == SkillTreeRevisionStatus.Draft);
                    if (revision is null)
                    {
                        revision = new SkillTreeRevision { SkillTree = tree };
                        var published = tree.Revisions.FirstOrDefault(item => item.Id == tree.CurrentPublishedRevisionId);
                        foreach (var reference in published?.Categories ?? [])
                            revision.Categories.Add(new SkillTreeCategoryRef { CategoryId = reference.CategoryId, SortOrder = reference.SortOrder });
                        tree.Revisions.Add(revision);
                    }
                }
                else
                {
                    tree = new SkillTree { Name = options.TreeName ?? package.Title, Summary = "从比赛导入的训练内容", IconKey = "flag" };
                    revision = new SkillTreeRevision { SkillTree = tree };
                    tree.Revisions.Add(revision);
                    db.SkillTrees.Add(tree);
                    AddMap("tree", sourceTreeId, tree.Id);
                }
                batch.TreeId = tree.Id;
            }
            var categoryGroups = package.Challenges.GroupBy(c => c.Category.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(c => c.Category).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.OrdinalIgnoreCase);
            string GroupKey(string category) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(TreeSourceId(package) + ":" + category)));
            foreach (var source in package.Challenges)
            {
                // Re-import preserves local organization as well as resource identity and scores.
                if (maps.ContainsKey(("challenge", source.SourceId))) continue;
                Skill skill;
                if (targetSkill is not null)
                    skill = targetSkill;
                else
                {
                    var groupKey = GroupKey(source.Category);
                    var variants = categoryGroups[source.Category.Trim()];
                    // Exact historical keys win. Unmapped variants reuse a mapped sibling within this package only.
                    var mappedKey = maps.ContainsKey(("skill", groupKey)) ? groupKey : maps.ContainsKey(("category", groupKey)) ? null : variants.Select(GroupKey).FirstOrDefault(k => maps.ContainsKey(("skill", k)));
                    if (mappedKey is not null && maps.TryGetValue(("skill", mappedKey), out var skillMap))
                        skill = db.Skills.Local.FirstOrDefault(item => item.Id == skillMap.TargetId && item.DeletedAtUtc == null) ?? await db.Skills.SingleOrDefaultAsync(item => item.Id == skillMap.TargetId && item.DeletedAtUtc == null, token)
                            ?? throw new InvalidOperationException("import.mapped_skill_missing");
                    else
                    {
                        var displayName = variants[0].Trim();
                        skill = new Skill { Name = displayName, IconKey = Icon(displayName) };
                        var categoryKey = maps.ContainsKey(("category", groupKey)) ? groupKey : variants.Select(GroupKey).FirstOrDefault(k => maps.ContainsKey(("category", k)));
                        SkillCategory category;
                        if (categoryKey is not null)
                        {
                            var categoryId = maps[("category", categoryKey)].TargetId;
                            category = db.SkillCategories.Local.FirstOrDefault(c => c.Id == categoryId && c.DeletedAtUtc == null && c.MergedIntoCategoryId == null)
                                ?? await db.SkillCategories.SingleOrDefaultAsync(c => c.Id == categoryId && c.DeletedAtUtc == null && c.MergedIntoCategoryId == null, token)
                                ?? throw new InvalidOperationException("import.mapped_category_missing");
                            var persistedOrder = await db.CategorySkillRefs.Where(r => r.CategoryId == category.Id).MaxAsync(r => (int?)r.SortOrder, token) ?? -1;
                            var localOrder = db.CategorySkillRefs.Local.Where(r => r.CategoryId == category.Id || r.Category == category).Select(r => r.SortOrder).DefaultIfEmpty(-1).Max();
                            db.CategorySkillRefs.Add(new CategorySkillRef { Category = category, Skill = skill, SortOrder = Math.Max(persistedOrder, localOrder) + 1 });
                        }
                        else
                        {
                            category = new SkillCategory { Name = displayName, IconKey = Icon(displayName), Skills = [new CategorySkillRef { Skill = skill }] };
                            db.SkillCategories.Add(category);
                        }
                        if (!revision!.Categories.Any(r => r.CategoryId == category.Id || r.Category == category))
                            revision.Categories.Add(new SkillTreeCategoryRef { Category = category, SortOrder = revision.Categories.Count });
                        if (!maps.ContainsKey(("category", groupKey))) AddMap("category", groupKey, category.Id);
                        AddMap("skill", groupKey, skill.Id);
                    }
                    if (!maps.ContainsKey(("skill", groupKey)))
                    {
                        AddMap("skill", groupKey, skill.Id);
                        if (mappedKey is not null && maps.TryGetValue(("category", mappedKey), out var categoryMap) && !maps.ContainsKey(("category", groupKey)))
                            AddMap("category", groupKey, categoryMap.TargetId);
                    }
                }
                Guid challengeId;
                if (maps.TryGetValue(("challenge", source.SourceId), out var mapped))
                    challengeId = mapped.TargetId;
                else
                {
                    var challenge = new CanonicalChallenge
                    {
                        Type = source.Type,
                        CtfCategory = Enum.TryParse<ChallengeCategory>(source.Category, true, out var category) && Enum.IsDefined(category) ? category : ChallengeCategory.Misc,
                        Difficulty = ReadDifficulty(source.Difficulty),
                        Score = source.Score,
                        IsEnabled = source.IsEnabled,
                        SubmissionLimit = source.SubmissionLimit,
                        SourceType = package.Format,
                        SourceId = source.SourceId,
                        SourceName = package.Title,
                        SourceMetadataJson = source.MetadataJson,
                        Localizations = [new() { Locale = "zh", Title = source.Title, Body = source.Body }, new() { Locale = "en", Title = source.Title, Body = source.Body }],
                        Hints = source.Hints.SelectMany((hint, index) => new[] { new ChallengeHint { Locale = "zh", SortOrder = index, Content = hint }, new ChallengeHint { Locale = "en", SortOrder = index, Content = hint } }).ToList()
                    };
                    var attachments = new List<object>();
                    foreach (var attachment in source.Attachments)
                    {
                        if (attachment.RemoteUrl is not null)
                        {
                            attachments.Add(new { attachment.FileName, Sha256 = "", StorageKey = "", attachment.RemoteUrl, DynamicPool = false, ResourceOnly = attachment.IsBodyResource, Flag = "supplementary" });
                            continue;
                        }
                        var key = $"platform-import/{batch.Id:N}/{attachment.Sha256.ToLowerInvariant()}";
                        if (!staged.Contains(key, StringComparer.Ordinal))
                        {
                            await using var stream = new MemoryStream(package.Files[attachment.ArchivePath], writable: false);
                            staged.Add(key);
                            await storage.WriteAsync(key, stream, cancellationToken: token);
                        }
                        var resourceUrl = $"/api/challenges/{challenge.Id}/resources/{attachment.Sha256.ToLowerInvariant()}";
                        foreach (var text in challenge.Localizations)
                        {
                            foreach (var original in (attachment.OriginalUrls ?? []).Append(attachment.OriginalUrl ?? "").Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).OrderByDescending(value => value.Length))
                                text.Body = text.Body.Replace(original, resourceUrl, StringComparison.Ordinal);
                        }
                        attachments.Add(new
                        {
                            attachment.FileName,
                            attachment.Sha256,
                            StorageKey = key,
                            DynamicPool = source.Type == ChallengeType.DynamicAttachment && attachment.Flag is not null,
                            ResourceOnly = attachment.IsBodyResource,
                            ContentType = ResourceContentType(package.Files[attachment.ArchivePath]),
                            Flag = attachment.Flag ?? source.Flags.FirstOrDefault(flag => flag.Kind == "static")?.Value ?? "supplementary"
                        });
                    }
                    var config = source.Container;
                    var template = source.Flags.FirstOrDefault(flag => flag.Kind == "template")?.Value;
                    challenge.RuntimeConfigurationJson = JsonSerializer.Serialize(new
                    {
                        Attachments = attachments,
                        config?.Image,
                        ExposedPort = config?.ExposedPort ?? 0,
                        Cpu = config?.Cpu ?? 0,
                        MemoryMb = config?.MemoryMb ?? 0,
                        StorageMb = config?.StorageMb ?? 0,
                        config?.NetworkMode,
                        config?.FlagMode,
                        FlagTemplate = template,
                        ImportSourceInstance = options.SourceInstance
                    });
                    foreach (var flag in source.Flags.Where(item => item.Kind == "static"))
                        challenge.Flags.Add(new ChallengeFlag
                        { Kind = ChallengeFlagKind.Static, Value = flag.Value, MetadataJson = flag.IgnoreCase ? "{\"IgnoreCase\":true}" : null });
                    if (source.Type == ChallengeType.DynamicContainer)
                        challenge.Flags.Add(new ChallengeFlag { Kind = ChallengeFlagKind.Template, Template = template });
                    if (source.Type == ChallengeType.DynamicAttachment)
                        challenge.Flags.Add(new ChallengeFlag { Kind = ChallengeFlagKind.DynamicAttachment, AttachmentPoolKey = challenge.Id.ToString("N"), MetadataJson = JsonSerializer.Serialize(attachments.Where((_, index) => source.Attachments[index].Flag is not null)) });
                    db.Challenges.Add(challenge);
                    challengeId = challenge.Id;
                    AddMap("challenge", source.SourceId, challengeId);
                }
                var localRefs = db.ChangeTracker.Entries<SkillContent>().Where(item => item.State != EntityState.Deleted).Select(item => item.Entity).Where(item => item.SkillId == skill.Id || item.Skill == skill).ToArray();
                if (!localRefs.Any(item => item.ChallengeId == challengeId) && !await db.SkillContents.AnyAsync(item => item.SkillId == skill.Id && item.ChallengeId == challengeId, token))
                {
                    var last = await db.SkillContents.Where(item => item.SkillId == skill.Id).MaxAsync(item => (int?)item.SortOrder, token) ?? -1;
                    var order = Math.Max(last, localRefs.Select(item => item.SortOrder).DefaultIfEmpty(-1).Max()) + 1;
                    db.SkillContents.Add(new SkillContent { Skill = skill, ChallengeId = challengeId, SortOrder = order });
                    skill.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }
            }
            await db.SaveChangesAsync(token);
            batch.UserCount = await accounts.ImportAsync(package, options, batch, token);
            await history.ImportAsync(package, options, batch, token);
            await (scoreMigration ?? new PlatformScoreMigrationService(db)).ImportAsync(package, options, batch, token);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            committed = true;
            memoryCache?.Remove("learning-leaderboard:score");
            memoryCache?.Remove("learning-leaderboard:solves");
            try
            {
                if (targetSkill is not null)
                foreach (var categoryId in await db.CategorySkillRefs.Where(item => item.SkillId == targetSkill.Id).Select(item => item.CategoryId).ToArrayAsync(token))
                    await cache.InvalidateByCategoryAsync(categoryId, token);
            }
            catch { logger?.LogWarning("Import batch {BatchId} committed but its display cache could not be refreshed.", batch.Id); }
            return ToInfo(batch);
        }
        catch
        {
            if (committed)
                throw;
            await transaction.RollbackAsync(CancellationToken.None);
            foreach (var key in staged)
            { try { await storage.DeleteAsync(key, CancellationToken.None); } catch { /* Never mask the import failure; orphaned batch files have no public route. */ } }
            throw;
        }
        void AddMap(string kind, string id, Guid targetId)
        {
            var map = new PlatformImportMap { SourceInstance = options.SourceInstance, Kind = kind, SourceId = id, TargetId = targetId, Batch = batch };
            db.PlatformImportMaps.Add(map);
            maps.Add((kind, id), map);
        }
    }
    public static PlatformImportBatchInfo ToInfo(PlatformImportBatch batch) => new(batch.Id, batch.SourceInstance, batch.Format, batch.Title, batch.CreatedAtUtc, batch.TreeId, batch.ChallengeCount, batch.UserCount, batch.TeamCount, batch.SubmissionCount, batch.AwardCount, batch.ReportJson);
    private static string ResourceContentType(byte[] bytes)
    {
        var value = bytes.AsSpan();
        if (value.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return "image/png";
        if (value.StartsWith(new byte[] { 255, 216, 255 }))
            return "image/jpeg";
        if (value.StartsWith("GIF87a"u8) || value.StartsWith("GIF89a"u8))
            return "image/gif";
        if (value.Length >= 12 && value[..4].SequenceEqual("RIFF"u8) && value[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";
        return "application/octet-stream";
    }
    private static string Icon(string category) => category.ToLowerInvariant() switch { "web" => "web", "pwn" => "pwn", "crypto" => "crypto", _ => "flag" };
    private static Difficulty ReadDifficulty(string value) => value.ToLowerInvariant() switch { "easy" => Difficulty.Easy, "hard" => Difficulty.Hard, _ => Difficulty.Normal };
    private static string TreeSourceId(PlatformImportPackage package)
    {
        if (package.Format != "GZCTF-GAME")
            return "site";
        using var doc = JsonDocument.Parse(package.MetadataJson);
        if (doc.RootElement.TryGetProperty("manifest", out var manifest) && manifest.TryGetProperty("game", out var game) && game.TryGetProperty("id", out var id))
            return id.ToString();
        return package.Title;
    }
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._:/-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceName();
}
