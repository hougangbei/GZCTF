using System.Linq;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.Features.Imports;
public sealed class ScoreBaselineSchemaTests
{
    [Fact]
    public void Frozen_score_and_progress_provenance_are_persisted_without_cascading_deletion()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=model_only;Username=test").Options);
        var baseline = db.Model.GetEntityTypes().SingleOrDefault(e => e.ClrType.Name == "PlatformScoreBaseline");
        Assert.NotNull(baseline);
        Assert.Contains(baseline.GetIndexes(), i => i.IsUnique && i.Properties.Count == 1 && i.Properties.Single().Name == "UserId");
        Assert.All(baseline.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        var migration = db.Model.GetEntityTypes().Single(e => e.ClrType.Name == "PlatformScoreMigration");
        Assert.Contains(migration.GetIndexes(), i => i.IsUnique && i.Properties.Count == 1 && i.Properties.Single().Name == "SourceInstance");
        var progress = db.Model.GetEntityTypes().Single(e => e.ClrType.Name == "ChallengeProgress");
        Assert.True(progress.FindProperty("ImportedScoreBaselineId")!.IsNullable);
    }
}
