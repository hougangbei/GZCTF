using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261006200000_AddChallengeScore")]
public sealed class AddChallengeScore : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Score",
            table: "Challenges",
            type: "integer",
            nullable: false,
            defaultValue: 100);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Challenges_Score_Range",
            table: "Challenges",
            sql: "\"Score\" >= 0 AND \"Score\" <= 10000");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Challenges_Score_Range",
            table: "Challenges");
        migrationBuilder.DropColumn(name: "Score", table: "Challenges");
    }
}
