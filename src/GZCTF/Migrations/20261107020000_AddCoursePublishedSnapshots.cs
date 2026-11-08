using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace GZCTF.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261107020000_AddCoursePublishedSnapshots")]
public sealed class AddCoursePublishedSnapshots : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("CoursePublishedVersions", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            SkillTreeId = table.Column<Guid>(type: "uuid", nullable: false),
            PublicDocumentJson = table.Column<string>(type: "text", nullable: false),
            PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table => {
            table.PrimaryKey("PK_CoursePublishedVersions", x => x.Id);
            table.ForeignKey("FK_CoursePublishedVersions_SkillTrees_SkillTreeId", x => x.SkillTreeId, "SkillTrees", "Id", onDelete: ReferentialAction.Cascade);
        });
        m.CreateIndex("IX_CoursePublishedVersions_SkillTreeId", "CoursePublishedVersions", "SkillTreeId");
        m.CreateTable("CoursePublishedContents", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            VersionId = table.Column<Guid>(type: "uuid", nullable: false),
            ContentId = table.Column<Guid>(type: "uuid", nullable: false),
            Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
            PayloadJson = table.Column<string>(type: "text", nullable: false)
        }, constraints: table => {
            table.PrimaryKey("PK_CoursePublishedContents", x => x.Id);
            table.ForeignKey("FK_CoursePublishedContents_CoursePublishedVersions_VersionId", x => x.VersionId, "CoursePublishedVersions", "Id", onDelete: ReferentialAction.Cascade);
        });
        m.CreateIndex("IX_CoursePublishedContents_VersionId_Kind_ContentId", "CoursePublishedContents", new[] { "VersionId", "Kind", "ContentId" }, unique: true);
        m.Sql("""
            INSERT INTO "CoursePublishedVersions" ("Id", "SkillTreeId", "PublishedAtUtc", "PublicDocumentJson")
            SELECT t."CurrentPublishedRevisionId", t."Id", COALESCE(r."PublishedAtUtc", r."CreatedAtUtc"),
              jsonb_build_object('name', t."Name", 'summary', t."Summary", 'iconKey', t."IconKey",
                'coverUrl', CASE WHEN t."CoverHash" ~ '^[0-9a-f]{64}$' THEN '/Assets/' || t."CoverHash" || '/skill-tree-cover.webp' ELSE NULL END,
                'categories', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                  'categoryId', cat."Id", 'name', cat."Name", 'summary', cat."Summary", 'iconKey', cat."IconKey",
                  'skills', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                    'skillId', s."Id", 'name', s."Name", 'summary', s."Summary", 'iconKey', s."IconKey", 'isEnabled', s."IsEnabled",
                    'contents', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                      'contentId', COALESCE(sc."ChallengeId", sc."LessonId"),
                      'kind', CASE WHEN sc."ChallengeId" IS NOT NULL THEN 'challenge' ELSE 'lesson' END,
                      'title', COALESCE((SELECT cl."Title" FROM "ChallengeLocalizations" cl WHERE cl."ChallengeId" = sc."ChallengeId" ORDER BY CASE cl."Locale" WHEN 'en' THEN 0 WHEN 'zh-CN' THEN 1 ELSE 2 END LIMIT 1),
                                        (SELECT ll."Title" FROM "LessonLocalizations" ll WHERE ll."LessonId" = sc."LessonId" ORDER BY CASE ll."Locale" WHEN 'en' THEN 0 WHEN 'zh-CN' THEN 1 ELSE 2 END LIMIT 1), ''),
                      'summary', COALESCE((SELECT cl."Summary" FROM "ChallengeLocalizations" cl WHERE cl."ChallengeId" = sc."ChallengeId" ORDER BY CASE cl."Locale" WHEN 'en' THEN 0 WHEN 'zh-CN' THEN 1 ELSE 2 END LIMIT 1), ''),
                      'expectedMinutes', COALESCE(ch."ExpectedMinutes", 0),
                      'difficulty', CASE ch."Difficulty" WHEN 0 THEN 'Baby' WHEN 1 THEN 'Trivial' WHEN 2 THEN 'Easy' WHEN 3 THEN 'Normal' WHEN 4 THEN 'Medium' WHEN 5 THEN 'Hard' WHEN 6 THEN 'Expert' WHEN 7 THEN 'Insane' ELSE '' END
                    ) ORDER BY sc."SortOrder") FROM "SkillContents" sc LEFT JOIN "Challenges" ch ON ch."Id" = sc."ChallengeId" LEFT JOIN "Lessons" l ON l."Id" = sc."LessonId"
                      WHERE sc."SkillId" = s."Id" AND ((ch."PublicationState" = 1 AND ch."IsEnabled") OR l."PublicationState" = 1)), '[]'::jsonb)
                    ) ORDER BY csr."SortOrder") FROM "CategorySkillRefs" csr JOIN "Skills" s ON s."Id" = csr."SkillId"
                    WHERE csr."CategoryId" = cat."Id" AND s."DeletedAtUtc" IS NULL AND s."IsEnabled"), '[]'::jsonb)
                  ) ORDER BY tr."SortOrder") FROM "SkillTreeCategoryRefs" tr JOIN "SkillCategories" cat ON cat."Id" = tr."CategoryId"
                  WHERE tr."RevisionId" = r."Id" AND cat."DeletedAtUtc" IS NULL), '[]'::jsonb))::text
            FROM "SkillTrees" t JOIN "SkillTreeRevisions" r ON r."Id" = t."CurrentPublishedRevisionId"
            WHERE t."DeletedAtUtc" IS NULL;

            INSERT INTO "CoursePublishedContents" ("Id", "VersionId", "ContentId", "Kind", "PayloadJson")
            SELECT gen_random_uuid(), v."Id", c."Id", 'challenge', jsonb_build_object(
              'challenge', to_jsonb(c) || jsonb_build_object('RuntimeConfigurationJson', c."RuntimeConfigurationJson"::text, 'SourceMetadataJson', c."SourceMetadataJson"::text),
              'localizations', COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM "ChallengeLocalizations" x WHERE x."ChallengeId" = c."Id"), '[]'::jsonb),
              'flags', COALESCE((SELECT jsonb_agg(to_jsonb(x) || jsonb_build_object('MetadataJson', x."MetadataJson"::text)) FROM "ChallengeFlags" x WHERE x."ChallengeId" = c."Id"), '[]'::jsonb),
              'hints', COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM "ChallengeHints" x WHERE x."ChallengeId" = c."Id"), '[]'::jsonb),
              'writeups', COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM "ChallengeWriteups" x WHERE x."ChallengeId" = c."Id"), '[]'::jsonb))::text
            FROM "CoursePublishedVersions" v JOIN "SkillTreeCategoryRefs" tr ON tr."RevisionId" = v."Id"
              JOIN "SkillCategories" cat ON cat."Id" = tr."CategoryId" AND cat."DeletedAtUtc" IS NULL
              JOIN "CategorySkillRefs" csr ON csr."CategoryId" = cat."Id" JOIN "Skills" s ON s."Id" = csr."SkillId" AND s."DeletedAtUtc" IS NULL AND s."IsEnabled"
              JOIN "SkillContents" sc ON sc."SkillId" = s."Id" JOIN "Challenges" c ON c."Id" = sc."ChallengeId" AND c."PublicationState" = 1 AND c."IsEnabled"
            GROUP BY v."Id", c."Id";
            INSERT INTO "CoursePublishedContents" ("Id", "VersionId", "ContentId", "Kind", "PayloadJson")
            SELECT gen_random_uuid(), v."Id", l."Id", 'lesson', jsonb_build_object('lesson', to_jsonb(l),
              'localizations', COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM "LessonLocalizations" x WHERE x."LessonId" = l."Id"), '[]'::jsonb))::text
            FROM "CoursePublishedVersions" v JOIN "SkillTreeCategoryRefs" tr ON tr."RevisionId" = v."Id"
              JOIN "SkillCategories" cat ON cat."Id" = tr."CategoryId" AND cat."DeletedAtUtc" IS NULL
              JOIN "CategorySkillRefs" csr ON csr."CategoryId" = cat."Id" JOIN "Skills" s ON s."Id" = csr."SkillId" AND s."DeletedAtUtc" IS NULL AND s."IsEnabled"
              JOIN "SkillContents" sc ON sc."SkillId" = s."Id" JOIN "Lessons" l ON l."Id" = sc."LessonId" AND l."PublicationState" = 1
            GROUP BY v."Id", l."Id";
            """);
    }
    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("CoursePublishedContents");
        m.DropTable("CoursePublishedVersions");
    }
}
