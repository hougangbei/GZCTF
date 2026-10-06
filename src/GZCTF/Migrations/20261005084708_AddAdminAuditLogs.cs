using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ActorKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TargetName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    ErrorReason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    RequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AffectedCount = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FlagAttemptLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RejectionCode = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    ProtectedSubmittedFlag = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlagAttemptLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActorId_OccurredAtUtc_Id",
                table: "AuditEvents",
                columns: new[] { "ActorId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Category_OccurredAtUtc_Id",
                table: "AuditEvents",
                columns: new[] { "Category", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredAtUtc_Id",
                table: "AuditEvents",
                columns: new[] { "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Succeeded_OccurredAtUtc_Id",
                table: "AuditEvents",
                columns: new[] { "Succeeded", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FlagAttemptLogs_ChallengeId_OccurredAtUtc_Id",
                table: "FlagAttemptLogs",
                columns: new[] { "ChallengeId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FlagAttemptLogs_OccurredAtUtc_Id",
                table: "FlagAttemptLogs",
                columns: new[] { "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FlagAttemptLogs_Outcome_OccurredAtUtc_Id",
                table: "FlagAttemptLogs",
                columns: new[] { "Outcome", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FlagAttemptLogs_UserId_OccurredAtUtc_Id",
                table: "FlagAttemptLogs",
                columns: new[] { "UserId", "OccurredAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "FlagAttemptLogs");
        }
    }
}
