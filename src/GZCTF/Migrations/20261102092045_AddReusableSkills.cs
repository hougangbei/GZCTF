using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddReusableSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverHash",
                table: "SkillTrees",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Skills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    IconKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Skills", x => x.Id);
                    table.CheckConstraint("CK_Skills_IconKey", "\"IconKey\" IN ('flag','web','crypto','pwn','brain','ai','terminal','network','cloud','shield','bug','code','database','linux','windows','mobile','reverse','forensics','osint','binary','key','lock','file','folder','book','target','trophy','rocket','lab','tools','chip','wifi','globe','server','container','chain','puzzle','lightning','star','compass')");
                });

            migrationBuilder.CreateTable(
                name: "CategorySkillRefs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategorySkillRefs", x => x.Id);
                    table.CheckConstraint("CK_CategorySkillRefs_Order", "\"SortOrder\" >= 0");
                    table.ForeignKey(
                        name: "FK_CategorySkillRefs_SkillCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "SkillCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CategorySkillRefs_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SkillContents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkillContents", x => x.Id);
                    table.CheckConstraint("CK_SkillContents_ExactlyOneContent", "(\"LessonId\" IS NOT NULL AND \"ChallengeId\" IS NULL) OR (\"LessonId\" IS NULL AND \"ChallengeId\" IS NOT NULL)");
                    table.CheckConstraint("CK_SkillContents_Order", "\"SortOrder\" >= 0");
                    table.ForeignKey(
                        name: "FK_SkillContents_Challenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "Challenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SkillContents_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SkillContents_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategorySkillRefs_CategoryId_SkillId",
                table: "CategorySkillRefs",
                columns: new[] { "CategoryId", "SkillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategorySkillRefs_CategoryId_SortOrder",
                table: "CategorySkillRefs",
                columns: new[] { "CategoryId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategorySkillRefs_SkillId",
                table: "CategorySkillRefs",
                column: "SkillId");

            migrationBuilder.CreateIndex(
                name: "IX_SkillContents_ChallengeId",
                table: "SkillContents",
                column: "ChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_SkillContents_LessonId",
                table: "SkillContents",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_SkillContents_SkillId_ChallengeId",
                table: "SkillContents",
                columns: new[] { "SkillId", "ChallengeId" },
                unique: true,
                filter: "\"ChallengeId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SkillContents_SkillId_LessonId",
                table: "SkillContents",
                columns: new[] { "SkillId", "LessonId" },
                unique: true,
                filter: "\"LessonId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SkillContents_SkillId_SortOrder",
                table: "SkillContents",
                columns: new[] { "SkillId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Skills_DeletedAtUtc",
                table: "Skills",
                column: "DeletedAtUtc");
            // Stable IDs preserve category deep links and content row history. Empty categories remain empty.
            migrationBuilder.Sql("""
                INSERT INTO "Skills" ("Id", "Name", "Summary", "IconKey", "IsEnabled", "CreatedAtUtc", "UpdatedAtUtc", "DeletedAtUtc")
                SELECT category."Id", category."Name", category."Summary", category."IconKey", TRUE,
                       category."CreatedAtUtc", category."UpdatedAtUtc", category."DeletedAtUtc"
                FROM "SkillCategories" category WHERE EXISTS
                    (SELECT 1 FROM "CategoryContents" content WHERE content."CategoryId" = category."Id");
                INSERT INTO "CategorySkillRefs" ("Id", "CategoryId", "SkillId", "SortOrder")
                SELECT "Id", "Id", "Id", 0 FROM "Skills";
                INSERT INTO "SkillContents" ("Id", "SkillId", "SortOrder", "LessonId", "ChallengeId", "CreatedAtUtc")
                SELECT "Id", "CategoryId", "SortOrder", "LessonId", "ChallengeId", "CreatedAtUtc" FROM "CategoryContents";
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("The reusable skill hierarchy cannot be reversed without losing authored skills, memberships and cover metadata. Restore the pre-upgrade database backup to roll back this migration.");
        }
    }
}
