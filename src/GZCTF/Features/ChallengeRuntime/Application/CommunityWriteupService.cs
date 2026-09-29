using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.ChallengeRuntime.Domain;
using GZCTF.Models;
using GZCTF.Storage.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.ChallengeRuntime.Application;

public sealed record CommunityWriteupSummary(
    Guid Id, string Title, string AuthorName, long FileSize, DateTimeOffset CreatedAtUtc);

public sealed record AdminCommunityWriteupSummary(
    Guid Id, Guid ChallengeId, string ChallengeTitle, string Title, string AuthorName,
    long FileSize, CommunityWriteupStatus Status, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReviewedAtUtc);

public sealed class CommunityWriteupService(AppDbContext db, IBlobStorage storage)
{
    public const long MaxPdfBytes = 20L * 1024 * 1024;

    public async Task<IReadOnlyList<CommunityWriteupSummary>?> ListApprovedAsync(
        Guid challengeId, CancellationToken token)
    {
        if (!await IsPublicChallengeAsync(challengeId, token)) return null;
        return await db.CommunityWriteups.AsNoTracking()
            .Where(item => item.ChallengeId == challengeId && item.Status == CommunityWriteupStatus.Approved)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Select(item => new CommunityWriteupSummary(
                item.Id, item.Title, item.AuthorName, item.FileSize, item.CreatedAtUtc))
            .ToArrayAsync(token);
    }

    public async Task<Guid?> SubmitAsync(Guid challengeId, string? title, string? authorName,
        IFormFile? file, CancellationToken token)
    {
        if (!await IsPublicChallengeAsync(challengeId, token)) return null;
        title = title?.Trim();
        authorName = authorName?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 160 ||
            string.IsNullOrWhiteSpace(authorName) || authorName.Length > 80 ||
            file is null || file.Length < 5 || file.Length > MaxPdfBytes ||
            !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A title, author name and PDF under 20 MB are required.");

        await using var content = new MemoryStream((int)file.Length);
        await file.CopyToAsync(content, token);
        content.Position = 0;
        var header = new byte[5];
        if (await content.ReadAsync(header, token) != header.Length ||
            !header.AsSpan().SequenceEqual("%PDF-"u8))
            throw new ArgumentException("The uploaded file is not a PDF.");

        var writeup = new CommunityWriteup
        {
            ChallengeId = challengeId,
            Title = title,
            AuthorName = authorName,
            FileSize = file.Length
        };
        writeup.StorageKey = StoragePath.Combine("community-writeups", writeup.Id.ToString("N")[..2],
            $"{writeup.Id:N}.pdf");
        content.Position = 0;
        await storage.WriteAsync(writeup.StorageKey, content, cancellationToken: token);
        try
        {
            db.CommunityWriteups.Add(writeup);
            await db.SaveChangesAsync(token);
        }
        catch
        {
            await storage.DeleteAsync(writeup.StorageKey, token);
            throw;
        }
        return writeup.Id;
    }

    public async Task<Stream?> OpenPdfAsync(Guid challengeId, Guid writeupId, bool isAdmin,
        CancellationToken token)
    {
        var item = await db.CommunityWriteups.AsNoTracking()
            .Where(value => value.Id == writeupId && value.ChallengeId == challengeId)
            .Select(value => new { value.StorageKey, value.Status })
            .FirstOrDefaultAsync(token);
        if (item is null || (!isAdmin &&
            (item.Status != CommunityWriteupStatus.Approved ||
             !await IsPublicChallengeAsync(challengeId, token))))
            return null;
        if (!await storage.ExistsAsync(item.StorageKey, token)) return null;
        return await storage.OpenReadAsync(item.StorageKey, token);
    }

    public async Task<IReadOnlyList<AdminCommunityWriteupSummary>> ListForReviewAsync(
        CommunityWriteupStatus? status, CancellationToken token)
    {
        var query = db.CommunityWriteups.AsNoTracking().Include(item => item.Challenge.Localizations)
            .AsQueryable();
        if (status is { } filter) query = query.Where(item => item.Status == filter);
        var items = await query.OrderByDescending(item => item.CreatedAtUtc).Take(200).ToArrayAsync(token);
        return items.Select(item => new AdminCommunityWriteupSummary(
            item.Id, item.ChallengeId,
            item.Challenge.Localizations.FirstOrDefault(localization => localization.Locale == "en")?.Title ??
            item.Challenge.Localizations.FirstOrDefault()?.Title ?? item.ChallengeId.ToString(),
            item.Title, item.AuthorName, item.FileSize, item.Status, item.CreatedAtUtc,
            item.ReviewedAtUtc)).ToArray();
    }

    public async Task<bool> ReviewAsync(Guid id, bool approved, Guid reviewerId, CancellationToken token)
    {
        var item = await db.CommunityWriteups.FindAsync([id], token);
        if (item is null) return false;
        item.Status = approved ? CommunityWriteupStatus.Approved : CommunityWriteupStatus.Rejected;
        item.ReviewedAtUtc = DateTimeOffset.UtcNow;
        item.ReviewedByUserId = reviewerId;
        await db.SaveChangesAsync(token);
        return true;
    }

    private Task<bool> IsPublicChallengeAsync(Guid id, CancellationToken token) =>
        db.Challenges.AsNoTracking().AnyAsync(item => item.Id == id &&
            item.PublicationState == ChallengePublicationState.Published && item.IsEnabled, token);
}
