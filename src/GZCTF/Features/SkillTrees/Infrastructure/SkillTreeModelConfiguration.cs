using GZCTF.Features.SkillTrees.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.SkillTrees.Infrastructure;

internal static class SkillTreeModelConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CourseStoredAsset>(entity =>
        {
            entity.HasKey(x => x.StorageKey);
            entity.Property(x => x.StorageKey).HasMaxLength(512);
            entity.Property(x => x.ReferenceToken).HasMaxLength(512).IsRequired();
            entity.HasIndex(x => x.LastTouchedUtc);
        });
        modelBuilder.Entity<CourseNodeOperation>(entity =>
        {
            entity.HasKey(x => new { x.SkillTreeId, x.OperationId });
            entity.Property(x => x.RequestHash).IsRequired().HasMaxLength(64);
            entity.Property(x => x.ResponseJson).IsRequired();
            entity.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.SkillTreeId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CourseWorkspaceContent>(entity =>
        {
            entity.HasKey(x => new { x.SkillTreeId, x.Kind, x.ContentId });
            entity.Property(x => x.Kind).HasMaxLength(16).IsRequired();
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.SkillTreeId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CoursePublishedVersion>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.PublicDocumentJson).IsRequired();
            entity.HasOne(x => x.SkillTree).WithMany().HasForeignKey(x => x.SkillTreeId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CoursePublishedContent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Kind).IsRequired().HasMaxLength(16);
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.HasIndex(x => new { x.VersionId, x.Kind, x.ContentId }).IsUnique();
            entity.HasOne(x => x.Version).WithMany(x => x.Contents).HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CourseAuthoringWorkspace>(entity =>
        {
            entity.HasKey(x => x.SkillTreeId);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.DocumentJson).IsRequired();
            entity.HasOne(x => x.SkillTree).WithOne().HasForeignKey<CourseAuthoringWorkspace>(x => x.SkillTreeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SkillTree>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Summary).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.IconKey).HasMaxLength(32).IsRequired();
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.Property(x => x.CoverHash).HasMaxLength(64);
            entity.HasIndex(x => x.DeletedAtUtc);
            entity.HasOne(x => x.CurrentPublishedRevision).WithMany()
                .HasForeignKey(x => x.CurrentPublishedRevisionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Revisions).WithOne(x => x.SkillTree)
                .HasForeignKey(x => x.SkillTreeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Enrollments).WithOne(x => x.SkillTree)
                .HasForeignKey(x => x.SkillTreeId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_SkillTrees_IconKey",
                "\"IconKey\" IN ('flag','web','crypto','pwn','brain','ai','terminal','network','cloud','shield','bug','code','database','linux','windows','mobile','reverse','forensics','osint','binary','key','lock','file','folder','book','target','trophy','rocket','lab','tools','chip','wifi','globe','server','container','chain','puzzle','lightning','star','compass')"));
        });

        modelBuilder.Entity<SkillTreeRevision>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Status).HasConversion<byte>();
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => x.SkillTreeId).IsUnique()
                .HasFilter($"\"Status\" = {(byte)SkillTreeRevisionStatus.Draft}");
        });

        modelBuilder.Entity<SkillCategory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Summary).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.IconKey).HasMaxLength(32).IsRequired();
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => x.DeletedAtUtc);
            entity.HasOne(x => x.MergedIntoCategory).WithMany()
                .HasForeignKey(x => x.MergedIntoCategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_SkillCategories_IconKey",
                "\"IconKey\" IN ('flag','web','crypto','pwn','brain','ai','terminal','network','cloud','shield','bug','code','database','linux','windows','mobile','reverse','forensics','osint','binary','key','lock','file','folder','book','target','trophy','rocket','lab','tools','chip','wifi','globe','server','container','chain','puzzle','lightning','star','compass')"));
        });

        modelBuilder.Entity<SkillTreeCategoryRef>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasIndex(x => new { x.RevisionId, x.CategoryId }).IsUnique();
            entity.HasIndex(x => new { x.RevisionId, x.SortOrder }).IsUnique();
            entity.HasOne(x => x.Revision).WithMany(x => x.Categories)
                .HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Category).WithMany(x => x.SkillTrees)
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CategoryContent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasIndex(x => new { x.CategoryId, x.SortOrder }).IsUnique();
            entity.HasIndex(x => new { x.CategoryId, x.ChallengeId }).IsUnique()
                .HasFilter("\"ChallengeId\" IS NOT NULL");
            entity.HasIndex(x => new { x.CategoryId, x.LessonId }).IsUnique()
                .HasFilter("\"LessonId\" IS NOT NULL");
            entity.HasOne(x => x.Category).WithMany(x => x.Contents)
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Challenge).WithMany()
                .HasForeignKey(x => x.ChallengeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Lesson).WithMany(x => x.CategoryContents)
                .HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_CategoryContents_ExactlyOneContent",
                "(\"LessonId\" IS NOT NULL AND \"ChallengeId\" IS NULL) OR " +
                "(\"LessonId\" IS NULL AND \"ChallengeId\" IS NOT NULL)"));
        });

        modelBuilder.Entity<Skill>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Summary).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.IconKey).HasMaxLength(32).IsRequired();
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => x.DeletedAtUtc);
            entity.ToTable(table => table.HasCheckConstraint("CK_Skills_IconKey",
                "\"IconKey\" IN (" + string.Join(",", SkillTreeIconCatalog.Values.Keys.Select(key => "'" + key + "'")) + ")"));
        });
        modelBuilder.Entity<CategorySkillRef>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasIndex(x => new { x.CategoryId, x.SkillId }).IsUnique();
            entity.HasIndex(x => new { x.CategoryId, x.SortOrder }).IsUnique();
            entity.HasOne(x => x.Category).WithMany(x => x.Skills).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Skill).WithMany(x => x.Categories).HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint("CK_CategorySkillRefs_Order", "\"SortOrder\" >= 0"));
        });
        modelBuilder.Entity<SkillContent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasIndex(x => new { x.SkillId, x.SortOrder }).IsUnique();
            entity.HasIndex(x => new { x.SkillId, x.ChallengeId }).IsUnique().HasFilter("\"ChallengeId\" IS NOT NULL");
            entity.HasIndex(x => new { x.SkillId, x.LessonId }).IsUnique().HasFilter("\"LessonId\" IS NOT NULL");
            entity.HasOne(x => x.Skill).WithMany(x => x.Contents).HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Challenge).WithMany().HasForeignKey(x => x.ChallengeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Lesson).WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => {
                table.HasCheckConstraint("CK_SkillContents_ExactlyOneContent", "(\"LessonId\" IS NOT NULL AND \"ChallengeId\" IS NULL) OR (\"LessonId\" IS NULL AND \"ChallengeId\" IS NOT NULL)");
                table.HasCheckConstraint("CK_SkillContents_Order", "\"SortOrder\" >= 0");
            });
        });

        modelBuilder.Entity<SkillTreeEnrollment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasIndex(x => new { x.UserId, x.SkillTreeId }).IsUnique();
            entity.HasIndex(x => x.UserId).IsUnique().HasFilter("\"IsCurrent\" = TRUE");
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LearningPathRedirect>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.OldSlug).HasMaxLength(128).IsRequired();
            entity.HasIndex(x => x.LearningPathId).IsUnique();
            entity.HasIndex(x => x.OldSlug).IsUnique();
            entity.HasOne(x => x.SkillTree).WithMany().HasForeignKey(x => x.SkillTreeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
