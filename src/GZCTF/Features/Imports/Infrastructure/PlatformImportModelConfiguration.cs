using GZCTF.Features.Imports.Domain;
using GZCTF.Features.SkillTrees.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.Imports.Infrastructure;

public static class PlatformImportModelConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<PlatformScoreBaseline>(entity =>
        {
            entity.Property(e => e.SourceInstance).HasMaxLength(128);
            entity.Property(e => e.SourceUserId).HasMaxLength(128);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasIndex(e => new { e.SourceInstance, e.SourceUserId }).IsUnique();
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PlatformImportBatch>().WithMany().HasForeignKey(e => e.BatchId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PlatformScoreMigration>(entity =>
        {
            entity.Property(e => e.SourceInstance).HasMaxLength(128);
            entity.Property(e => e.ArchiveFingerprint).HasMaxLength(64);
            entity.Property(e => e.CanonicalSkipUserIdsHash).HasMaxLength(64);
            entity.HasIndex(e => e.SourceInstance).IsUnique();
            entity.HasOne<PlatformImportBatch>().WithMany().HasForeignKey(e => e.BatchId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<GZCTF.Features.LearningProgress.Domain.ChallengeProgress>()
            .HasOne(e => e.ImportedScoreBaseline).WithMany().HasForeignKey(e => e.ImportedScoreBaselineId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PlatformImportBatch>(entity =>
        {
            entity.Property(e => e.SourceInstance).HasMaxLength(128);
            entity.Property(e => e.Format).HasMaxLength(32);
            entity.Property(e => e.Fingerprint).HasMaxLength(64);
            entity.Property(e => e.PlanFingerprint).HasMaxLength(64);
            entity.Property(e => e.Title).HasMaxLength(256);
            entity.Property(e => e.ReportJson).HasColumnType("text");
            entity.Property(e => e.ProtectedMetadata).HasColumnType("text");
            entity.HasIndex(e => new { e.SourceInstance, e.Fingerprint, e.PlanFingerprint }).IsUnique();
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasOne<UserInfo>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<SkillTree>().WithMany().HasForeignKey(e => e.TreeId).OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<PlatformImportMap>(entity =>
        {
            entity.Property(e => e.SourceInstance).HasMaxLength(128);
            entity.Property(e => e.Kind).HasMaxLength(32);
            entity.Property(e => e.SourceId).HasMaxLength(128);
            entity.HasIndex(e => new { e.SourceInstance, e.Kind, e.SourceId }).IsUnique();
            entity.HasOne(e => e.Batch).WithMany().HasForeignKey(e => e.BatchId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<PlatformHistoryMember>(entity =>
        {
            entity.Property(e => e.SourceUserId).HasMaxLength(128);
            entity.Property(e => e.SourceUserName).HasMaxLength(256);
            entity.Property(e => e.SourceTeamId).HasMaxLength(128);
            entity.Property(e => e.SourceTeamName).HasMaxLength(256);
            entity.Property(e => e.ProtectedMetadata).HasColumnType("text");
            entity.HasIndex(e => new { e.BatchId, e.SourceUserId }).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.BatchId, e.SourceTeamId });
            entity.HasOne(e => e.Batch).WithMany().HasForeignKey(e => e.BatchId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserInfo>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<PlatformHistorySubmission>(entity =>
        {
            entity.Property(e => e.SourceId).HasMaxLength(128);
            entity.Property(e => e.SourceUserId).HasMaxLength(128);
            entity.Property(e => e.SourceUserName).HasMaxLength(256);
            entity.Property(e => e.SourceTeamId).HasMaxLength(128);
            entity.Property(e => e.SourceTeamName).HasMaxLength(256);
            entity.Property(e => e.SourceChallengeId).HasMaxLength(128);
            entity.Property(e => e.ChallengeTitle).HasMaxLength(256);
            entity.Property(e => e.Result).HasMaxLength(64);
            entity.Property(e => e.ProtectedMetadata).HasColumnType("text");
            entity.HasIndex(e => new { e.BatchId, e.SourceId }).IsUnique();
            entity.HasIndex(e => new { e.BatchId, e.SourceUserId });
            entity.HasIndex(e => new { e.BatchId, e.SourceTeamId });
            entity.HasOne(e => e.Batch).WithMany().HasForeignKey(e => e.BatchId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<PlatformHistoryAward>(entity =>
        {
            entity.Property(e => e.SourceId).HasMaxLength(128);
            entity.Property(e => e.SourceUserId).HasMaxLength(128);
            entity.Property(e => e.SourceUserName).HasMaxLength(256);
            entity.Property(e => e.SourceTeamId).HasMaxLength(128);
            entity.Property(e => e.SourceTeamName).HasMaxLength(256);
            entity.Property(e => e.Name).HasMaxLength(256);
            entity.Property(e => e.ProtectedMetadata).HasColumnType("text");
            entity.HasIndex(e => new { e.BatchId, e.SourceId }).IsUnique();
            entity.HasIndex(e => new { e.BatchId, e.SourceUserId });
            entity.HasIndex(e => new { e.BatchId, e.SourceTeamId });
            entity.HasOne(e => e.Batch).WithMany().HasForeignKey(e => e.BatchId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
