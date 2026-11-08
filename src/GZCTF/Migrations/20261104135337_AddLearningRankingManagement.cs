using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningRankingManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ImportedFrozenScore",
                table: "ChallengeProgress",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RankingLevelSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ThresholdsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankingLevelSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RankingOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DetailsJson = table.Column<string>(type: "text", nullable: true),
                    Delta = table.Column<long>(type: "bigint", nullable: false),
                    BeforeScore = table.Column<long>(type: "bigint", nullable: false),
                    AfterScore = table.Column<long>(type: "bigint", nullable: false),
                    SolvedDelta = table.Column<int>(type: "integer", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversesId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankingOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RankingScoreEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScoreDelta = table.Column<long>(type: "bigint", nullable: false),
                    SolvedDelta = table.Column<int>(type: "integer", nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankingScoreEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RankingSolveCorrections",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportedScoreBaselineId = table.Column<Guid>(type: "uuid", nullable: true),
                    FrozenScore = table.Column<int>(type: "integer", nullable: false),
                    OriginalSolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttributionMetadataJson = table.Column<string>(type: "text", nullable: true),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankingSolveCorrections", x => new { x.UserId, x.ChallengeId });
                });

            migrationBuilder.CreateTable(
                name: "RankingUserStates",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Hidden = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankingUserStates", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RankingOperations_OperationId",
                table: "RankingOperations",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RankingOperations_ReversesId",
                table: "RankingOperations",
                column: "ReversesId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RankingOperations_UserId_CreatedAtUtc",
                table: "RankingOperations",
                columns: new[] { "UserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RankingLevelSettings");

            migrationBuilder.DropTable(
                name: "RankingOperations");

            migrationBuilder.DropTable(
                name: "RankingScoreEvents");

            migrationBuilder.DropTable(
                name: "RankingSolveCorrections");

            migrationBuilder.DropTable(
                name: "RankingUserStates");

            migrationBuilder.DropColumn(
                name: "ImportedFrozenScore",
                table: "ChallengeProgress");
        }
    }
}
