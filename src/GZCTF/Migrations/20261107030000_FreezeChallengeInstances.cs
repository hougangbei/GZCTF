using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace GZCTF.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261107030000_FreezeChallengeInstances")]
public sealed class FreezeChallengeInstances : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>("PublishedChallengeJson", "UserChallengeInstances", type: "text", nullable: true);
        m.DropForeignKey("FK_ChallengeHelpUsages_ChallengeHints_HintId", "ChallengeHelpUsages");
        m.DropIndex("IX_ChallengeHelpUsages_HintId", "ChallengeHelpUsages");
        // Preserve the exact startup flags already stored on running instances. The initial course
        // snapshot captures their old configuration before authoring can mutate the library.
        m.Sql("""
            UPDATE "UserChallengeInstances" i SET "PublishedChallengeJson" = (
              SELECT c."PayloadJson" FROM "CoursePublishedContents" c
              JOIN "CoursePublishedVersions" v ON v."Id" = c."VersionId"
              JOIN "SkillTrees" t ON t."Id" = v."SkillTreeId"
              WHERE c."Kind" = 'challenge' AND c."ContentId" = i."ChallengeId"
                AND t."CurrentPublishedRevisionId" = v."Id" AND t."DeletedAtUtc" IS NULL
              ORDER BY c."VersionId" LIMIT 1);
            """);
    }
    protected override void Down(MigrationBuilder m)
    {
        m.DropColumn("PublishedChallengeJson", "UserChallengeInstances");
        m.Sql("""
            UPDATE "ChallengeHelpUsages" u SET "HintId" = NULL WHERE "HintId" IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM "ChallengeHints" h WHERE h."Id" = u."HintId");
            """);
        m.CreateIndex("IX_ChallengeHelpUsages_HintId", "ChallengeHelpUsages", "HintId");
        m.AddForeignKey("FK_ChallengeHelpUsages_ChallengeHints_HintId", "ChallengeHelpUsages", "HintId", "ChallengeHints", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
    }
}
