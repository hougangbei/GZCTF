using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations;

public partial class AddAboutPage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AboutPageStates",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                DraftJson = table.Column<string>(type: "text", nullable: false),
                DraftRevision = table.Column<long>(type: "bigint", nullable: false),
                CurrentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                LockOwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                LockOwnerName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                LockCreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                DraftUpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AboutPageStates", x => x.Id));

        migrationBuilder.CreateTable(
            name: "AboutPageVersions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                VersionNumber = table.Column<long>(type: "bigint", nullable: false),
                DocumentJson = table.Column<string>(type: "text", nullable: false),
                PublisherId = table.Column<Guid>(type: "uuid", nullable: false),
                PublisherName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                SourceDraftRevision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AboutPageVersions", x => x.Id));

        migrationBuilder.CreateIndex("IX_AboutPageStates_CurrentVersionId", "AboutPageStates", "CurrentVersionId");
        migrationBuilder.CreateIndex("IX_AboutPageVersions_VersionNumber", "AboutPageVersions", "VersionNumber", unique: true);
        migrationBuilder.Sql("""
            INSERT INTO "AboutPageStates" ("Id", "DraftJson", "DraftRevision", "DraftUpdatedAtUtc")
            VALUES (1, '{"schemaVersion":1,"title":"关于实验室","sections":[]}', 0, '2026-10-06 00:00:00+00');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AboutPageStates");
        migrationBuilder.DropTable("AboutPageVersions");
    }
}
