using System;
using System.Linq;
using GZCTF.Features.Imports.Domain;
using GZCTF.Models;
using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.Features.Imports;

public sealed class PlatformImportSchemaTests
{
    [Fact]
    public void Source_mapping_and_batch_identity_are_unique_and_history_has_local_user_fk()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=test").Options);
        var map = db.Model.FindEntityType(typeof(PlatformImportMap))!;
        Assert.Contains(map.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(p => p.Name).SequenceEqual(new[] { "SourceInstance", "Kind", "SourceId" }));
        var batch = db.Model.FindEntityType(typeof(PlatformImportBatch))!;
        Assert.Contains(batch.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(p => p.Name).SequenceEqual(new[] { "SourceInstance", "Fingerprint", "PlanFingerprint" }));
        var member = db.Model.FindEntityType(typeof(PlatformHistoryMember))!;
        Assert.Contains(member.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(UserInfo) &&
            key.DeleteBehavior == DeleteBehavior.SetNull);
        Assert.DoesNotContain(db.Model.FindEntityType(typeof(PlatformHistorySubmission))!.GetForeignKeys(),
            key => key.PrincipalEntityType.Name.Contains("ChallengeProgress", StringComparison.Ordinal));
    }
}
