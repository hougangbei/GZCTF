using System.Text.Json;
using GZCTF.Features.SkillTrees.Api;
using System.Data;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.LearningPaths.Domain;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.SkillTrees.Application;

public sealed class SkillTreeEnrollmentService(AppDbContext db)
{
    private const int RecentActivityLimit = 20;

    public async Task<IReadOnlyList<SkillTreeEnrollmentResponse>> ListAsync(
        Guid userId, CancellationToken token)
    {
        var enrollments = await db.SkillTreeEnrollments.AsNoTracking().Where(e => e.UserId == userId && e.SkillTree.DeletedAtUtc == null)
            .OrderBy(e => e.EnrolledAtUtc).ToArrayAsync(token);
        var query = new SkillTreeQueryService(db);
        var result = new List<SkillTreeEnrollmentResponse>();
        foreach (var enrollment in enrollments)
        {
            var tree = await query.GetPublishedAsync(enrollment.SkillTreeId, token);
            if (tree is not null) result.Add(new(enrollment.Id, tree.SkillTreeId, tree.Name, tree.IconKey, enrollment.IsCurrent, enrollment.EnrolledAtUtc));
        }
        return result;
    }

    public async Task<SkillTreeEnrollmentResponse?> EnrollAsync(
        Guid userId, Guid skillTreeId, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);

        var tree = await db.SkillTrees
            .FirstOrDefaultAsync(item => item.Id == skillTreeId &&
                                          item.DeletedAtUtc == null &&
                                          item.CurrentPublishedRevisionId != null, token);
        if (tree is null)
            return null;

        var enrollment = await db.SkillTreeEnrollments
            .FirstOrDefaultAsync(item => item.UserId == userId && item.SkillTreeId == skillTreeId, token);

        if (enrollment is null)
        {
            var hasCurrent = await db.SkillTreeEnrollments
                .AnyAsync(item => item.UserId == userId && item.IsCurrent &&
                                  item.SkillTree.DeletedAtUtc == null, token);
            enrollment = new SkillTreeEnrollment
            {
                UserId = userId,
                SkillTreeId = skillTreeId,
                IsCurrent = !hasCurrent
            };
            db.SkillTreeEnrollments.Add(enrollment);
        }
        else if (!enrollment.IsCurrent)
        {
            var hasCurrent = await db.SkillTreeEnrollments
                .AnyAsync(item => item.UserId == userId && item.IsCurrent &&
                                  item.SkillTree.DeletedAtUtc == null, token);
            if (!hasCurrent)
                enrollment.IsCurrent = true;
        }

        try
        {
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new SkillTreeRevisionConflictException();
        }

        var publishedTree = await new SkillTreeQueryService(db).GetPublishedAsync(tree.Id, token);
        return publishedTree is null ? null : new SkillTreeEnrollmentResponse(
            enrollment.Id, tree.Id, publishedTree.Name, publishedTree.IconKey, enrollment.IsCurrent, enrollment.EnrolledAtUtc);
    }

    public async Task<bool> LeaveAsync(Guid userId, Guid skillTreeId, CancellationToken token)
    {
        var enrollment = await db.SkillTreeEnrollments
            .FirstOrDefaultAsync(item => item.UserId == userId && item.SkillTreeId == skillTreeId, token);
        if (enrollment is null)
            return false;

        db.SkillTreeEnrollments.Remove(enrollment);
        await db.SaveChangesAsync(token);
        return true;
    }

    public async Task<SkillTreeEnrollmentResponse?> SelectCurrentAsync(
        Guid userId, Guid skillTreeId, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);

        var enrollment = await db.SkillTreeEnrollments
            .Include(item => item.SkillTree)
            .FirstOrDefaultAsync(item => item.UserId == userId && item.SkillTreeId == skillTreeId, token);
        if (enrollment is null)
            return null;

        await db.SkillTreeEnrollments
            .Where(item => item.UserId == userId && item.IsCurrent && item.Id != enrollment.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsCurrent, false), token);
        enrollment.IsCurrent = true;

        try
        {
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new SkillTreeRevisionConflictException();
        }

        var publishedTree = await new SkillTreeQueryService(db).GetPublishedAsync(enrollment.SkillTreeId, token);
        return publishedTree is null ? null : new SkillTreeEnrollmentResponse(
            enrollment.Id, enrollment.SkillTreeId, publishedTree.Name,
            publishedTree.IconKey, enrollment.IsCurrent, enrollment.EnrolledAtUtc);
    }

    /// <summary>
    /// Resolves whether a learner may open retained lesson content through a published tree.
    /// </summary>
    public async Task<LessonAccess> GetLessonAccessAsync(
        Guid lessonId, CancellationToken token)
    {
        var reachable = await GZCTF.Features.ChallengeRuntime.Application.PublishedCourseContentReader.ActiveContents(db)
            .AnyAsync(content => content.Kind == "lesson" && content.ContentId == lessonId, token);

        return reachable ? LessonAccess.Allowed : LessonAccess.NotFound;
    }

    public async Task<MyLearningResponse> GetMyLearningAsync(Guid userId, CancellationToken token)
    {
        var rows = await (from tree in db.SkillTrees.AsNoTracking()
            join version in db.CoursePublishedVersions.AsNoTracking() on tree.CurrentPublishedRevisionId equals version.Id
            select new { tree.Id, tree.DeletedAtUtc, version.PublicDocumentJson }).ToArrayAsync(token);
        var enrollments = await db.SkillTreeEnrollments.AsNoTracking().Where(e => e.UserId == userId).ToArrayAsync(token);
        var enrollmentByTree = enrollments.ToDictionary(e => e.SkillTreeId);
        var completedChallenges = await db.ChallengeProgress.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.ChallengeId).ToHashSetAsync(token);
        var completedLessons = await db.LessonProgress.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.LessonId).ToHashSetAsync(token);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var records = rows.Where(r => r.DeletedAtUtc == null || enrollmentByTree.ContainsKey(r.Id)).Select(row =>
        {
            var doc = JsonSerializer.Deserialize<CourseDocument>(row.PublicDocumentJson, options)!;
            var detail = SkillTreeQueryService.ToDetail(row.Id, doc);
            var all = detail.Categories.SelectMany(c => c.Contents).DistinctBy(c => (c.Kind, c.ContentId)).ToArray();
            bool Completed(SkillTreeContentSummaryResponse content) => content.Kind == "lesson" ? completedLessons.Contains(content.ContentId) : completedChallenges.Contains(content.ContentId);
            var challenges = all.Where(c => c.Kind == "challenge").ToArray(); var lessons = all.Where(c => c.Kind == "lesson").ToArray();
            var enrollment = enrollmentByTree.GetValueOrDefault(row.Id);
            return new MySkillTreeRecordResponse(row.Id, detail.Name, detail.IconKey, enrollment?.IsCurrent == true && row.DeletedAtUtc == null,
                row.DeletedAtUtc != null, detail.Categories.Count, detail.Categories.Count(c => c.Contents.Count > 0 && c.Contents.All(Completed)),
                challenges.Length, challenges.Count(Completed), lessons.Length, lessons.Count(Completed));
        }).OrderBy(r => r.Name).ThenBy(r => r.SkillTreeId).ToArray();
        var current = records.FirstOrDefault(r => r.IsCurrent)?.SkillTreeId;
        return new MyLearningResponse(current, records, await BuildRecentActivityAsync(userId, token));
    }

    public async Task<SkillTreeProgressResponse?> GetTreeProgressAsync(Guid userId, Guid treeId, CancellationToken token)
    {
        var tree = await new SkillTreeQueryService(db).GetPublishedAsync(treeId, token);
        if (tree is null) return null;
        var contents = tree.Categories.SelectMany(category => category.Contents).DistinctBy(content => (content.Kind, content.ContentId)).ToArray();
        var lessonIds = contents.Where(content => content.Kind == "lesson").Select(content => content.ContentId).ToArray();
        var challengeIds = contents.Where(content => content.Kind == "challenge").Select(content => content.ContentId).ToArray();
        var completedLessons = await db.LessonProgress.AsNoTracking().Where(progress => progress.UserId == userId && lessonIds.Contains(progress.LessonId))
            .Select(progress => progress.LessonId).Distinct().ToListAsync(token);
        var solvedChallenges = await db.ChallengeProgress.AsNoTracking().Where(progress => progress.UserId == userId && challengeIds.Contains(progress.ChallengeId))
            .Select(progress => progress.ChallengeId).Distinct().ToListAsync(token);
        bool Completed(SkillTreeContentSummaryResponse content) => content.Kind == "lesson" ? completedLessons.Contains(content.ContentId) : solvedChallenges.Contains(content.ContentId);
        var categories = tree.Categories.Select(category => new SkillCategoryProgressCount(category.CategoryId, category.Contents.Count, category.Contents.Count(Completed))).ToArray();
        var skills = tree.Categories.SelectMany(category => category.Skills ?? []).DistinctBy(skill => skill.SkillId)
            .Select(skill => new SkillTreeProgressCount(skill.SkillId, skill.Contents.Count, skill.Contents.Count(Completed))).ToArray();
        return new(contents.Length, contents.Count(Completed), completedLessons, solvedChallenges, categories, skills);
    }

    private async Task<IReadOnlyList<RecentLearningActivityResponse>> BuildRecentActivityAsync(
        Guid userId, CancellationToken token)
    {
        var lessons = await db.LessonProgress
            .AsNoTracking()
            .Include(progress => progress.Lesson)
                .ThenInclude(lesson => lesson.Localizations)
            .Where(progress => progress.UserId == userId)
            .OrderByDescending(progress => progress.CompletedAtUtc)
            .Take(RecentActivityLimit)
            .ToListAsync(token);

        var challenges = await db.ChallengeProgress
            .AsNoTracking()
            .Include(progress => progress.Challenge)
                .ThenInclude(challenge => challenge.Localizations)
            .Where(progress => progress.UserId == userId)
            .OrderByDescending(progress => progress.SolvedAtUtc)
            .Take(RecentActivityLimit)
            .ToListAsync(token);

        var titles = new Dictionary<(string Kind, Guid Id), string>();
        foreach (var tree in await new SkillTreeQueryService(db).ListPublishedAsync(token))
        {
            var detail = await new SkillTreeQueryService(db).GetPublishedAsync(tree.SkillTreeId, token);
            if (detail is null) continue;
            foreach (var content in detail.Categories.SelectMany(c => c.Contents)) titles.TryAdd((content.Kind, content.ContentId), content.Title);
        }
        return lessons.Select(p => new RecentLearningActivityResponse("lesson", p.LessonId,
                titles.GetValueOrDefault(("lesson", p.LessonId), "Lesson"), p.CompletedAtUtc, null))
            .Concat(challenges.Select(p => new RecentLearningActivityResponse("challenge", p.ChallengeId,
                titles.GetValueOrDefault(("challenge", p.ChallengeId), "Challenge"), p.SolvedAtUtc, p.SolveMode.ToString())))
            .OrderByDescending(item => item.CompletedAtUtc).Take(RecentActivityLimit).ToArray();
    }

    private static SkillTreeRevision? ResolveRevision(SkillTree tree)
    {
        if (tree.CurrentPublishedRevisionId is { } currentId)
        {
            var current = tree.Revisions.FirstOrDefault(revision => revision.Id == currentId);
            if (current is not null)
                return current;
        }

        return tree.Revisions
            .Where(revision => revision.Status == SkillTreeRevisionStatus.Published)
            .OrderByDescending(revision => revision.PublishedAtUtc ?? revision.CreatedAtUtc)
            .FirstOrDefault();
    }

    private static string PickTitle(IEnumerable<(string Locale, string Title)>? values)
    {
        if (values is null)
            return string.Empty;
        var list = values.ToList();
        return (list.FirstOrDefault(item => item.Locale.Equals("en", StringComparison.OrdinalIgnoreCase)).Title
                ?? list.FirstOrDefault(item => item.Locale.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)).Title
                ?? list.FirstOrDefault().Title) ?? string.Empty;
    }
}

public enum LessonAccess
{
    NotFound,
    EnrollmentRequired,
    Allowed
}
