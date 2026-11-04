using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformScoreBaselines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImportedScoreBaselineId",
                table: "ChallengeProgress",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlatformScoreBaselines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceInstance = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    SolvedCount = table.Column<int>(type: "integer", nullable: false),
                    Hidden = table.Column<bool>(type: "boolean", nullable: false),
                    HasActivity = table.Column<bool>(type: "boolean", nullable: false),
                    LastScoringAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformScoreBaselines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformScoreBaselines_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformScoreBaselines_PlatformImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "PlatformImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformScoreMigrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceInstance = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ArchiveFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CanonicalSkipUserIdsHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformScoreMigrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformScoreMigrations_PlatformImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "PlatformImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeProgress_ImportedScoreBaselineId",
                table: "ChallengeProgress",
                column: "ImportedScoreBaselineId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformScoreBaselines_BatchId",
                table: "PlatformScoreBaselines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformScoreBaselines_SourceInstance_SourceUserId",
                table: "PlatformScoreBaselines",
                columns: new[] { "SourceInstance", "SourceUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformScoreBaselines_UserId",
                table: "PlatformScoreBaselines",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformScoreMigrations_BatchId",
                table: "PlatformScoreMigrations",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformScoreMigrations_SourceInstance",
                table: "PlatformScoreMigrations",
                column: "SourceInstance",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ChallengeProgress_PlatformScoreBaselines_ImportedScoreBasel~",
                table: "ChallengeProgress",
                column: "ImportedScoreBaselineId",
                principalTable: "PlatformScoreBaselines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChallengeProgress_PlatformScoreBaselines_ImportedScoreBasel~",
                table: "ChallengeProgress");

            migrationBuilder.DropTable(
                name: "PlatformScoreBaselines");

            migrationBuilder.DropTable(
                name: "PlatformScoreMigrations");

            migrationBuilder.DropIndex(
                name: "IX_ChallengeProgress_ImportedScoreBaselineId",
                table: "ChallengeProgress");

            migrationBuilder.DropColumn(
                name: "ImportedScoreBaselineId",
                table: "ChallengeProgress");
        }
    }
}
