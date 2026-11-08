using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceInstance = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TreeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChallengeCount = table.Column<int>(type: "integer", nullable: false),
                    UserCount = table.Column<int>(type: "integer", nullable: false),
                    TeamCount = table.Column<int>(type: "integer", nullable: false),
                    SubmissionCount = table.Column<int>(type: "integer", nullable: false),
                    AwardCount = table.Column<int>(type: "integer", nullable: false),
                    ReportJson = table.Column<string>(type: "text", nullable: false),
                    ProtectedMetadata = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformImportBatches_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlatformImportBatches_SkillTrees_TreeId",
                        column: x => x.TreeId,
                        principalTable: "SkillTrees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PlatformHistoryAwards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SourceUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SourceTeamId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SourceTeamName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false),
                    AwardedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProtectedMetadata = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformHistoryAwards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformHistoryAwards_PlatformImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "PlatformImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformHistoryMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SourceTeamId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SourceTeamName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProtectedMetadata = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformHistoryMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformHistoryMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlatformHistoryMembers_PlatformImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "PlatformImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformHistorySubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SourceUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SourceTeamId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SourceTeamName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SourceChallengeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ChallengeTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Result = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    ProtectedMetadata = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformHistorySubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformHistorySubmissions_PlatformImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "PlatformImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformImportMaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceInstance = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformImportMaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformImportMaps_PlatformImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "PlatformImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformHistoryAwards_BatchId_SourceId",
                table: "PlatformHistoryAwards",
                columns: new[] { "BatchId", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformHistoryAwards_BatchId_SourceTeamId",
                table: "PlatformHistoryAwards",
                columns: new[] { "BatchId", "SourceTeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformHistoryAwards_BatchId_SourceUserId",
                table: "PlatformHistoryAwards",
                columns: new[] { "BatchId", "SourceUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformHistoryMembers_BatchId_SourceUserId",
                table: "PlatformHistoryMembers",
                columns: new[] { "BatchId", "SourceUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformHistoryMembers_UserId_BatchId_SourceTeamId",
                table: "PlatformHistoryMembers",
                columns: new[] { "UserId", "BatchId", "SourceTeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformHistorySubmissions_BatchId_SourceId",
                table: "PlatformHistorySubmissions",
                columns: new[] { "BatchId", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformHistorySubmissions_BatchId_SourceTeamId",
                table: "PlatformHistorySubmissions",
                columns: new[] { "BatchId", "SourceTeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformHistorySubmissions_BatchId_SourceUserId",
                table: "PlatformHistorySubmissions",
                columns: new[] { "BatchId", "SourceUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportBatches_CreatedAtUtc",
                table: "PlatformImportBatches",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportBatches_SourceInstance_Fingerprint_PlanFinger~",
                table: "PlatformImportBatches",
                columns: new[] { "SourceInstance", "Fingerprint", "PlanFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportBatches_TreeId",
                table: "PlatformImportBatches",
                column: "TreeId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportBatches_UserId",
                table: "PlatformImportBatches",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportMaps_BatchId",
                table: "PlatformImportMaps",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportMaps_SourceInstance_Kind_SourceId",
                table: "PlatformImportMaps",
                columns: new[] { "SourceInstance", "Kind", "SourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformHistoryAwards");

            migrationBuilder.DropTable(
                name: "PlatformHistoryMembers");

            migrationBuilder.DropTable(
                name: "PlatformHistorySubmissions");

            migrationBuilder.DropTable(
                name: "PlatformImportMaps");

            migrationBuilder.DropTable(
                name: "PlatformImportBatches");
        }
    }
}
