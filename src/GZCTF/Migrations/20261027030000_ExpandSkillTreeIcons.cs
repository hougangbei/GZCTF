using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261027030000_ExpandSkillTreeIcons")]
public sealed class ExpandSkillTreeIcons : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "SkillTrees", "SkillCategories" })
        {
            migrationBuilder.DropCheckConstraint($"CK_{table}_IconKey", table);
            migrationBuilder.AddCheckConstraint($"CK_{table}_IconKey", table,
                "\"IconKey\" IN ('flag','web','crypto','pwn','brain','ai','terminal','network','cloud','shield','bug','code','database','linux','windows','mobile','reverse','forensics','osint','binary','key','lock','file','folder','book','target','trophy','rocket','lab','tools','chip','wifi','globe','server','container','chain','puzzle','lightning','star','compass')");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "SkillTrees", "SkillCategories" })
        {
            migrationBuilder.DropCheckConstraint($"CK_{table}_IconKey", table);
            // Preserve records when rolling back to the original six-key catalog.
            migrationBuilder.Sql($"UPDATE \"{table}\" SET \"IconKey\" = 'flag' WHERE \"IconKey\" NOT IN ('flag','web','crypto','pwn','brain','ai')");
            migrationBuilder.AddCheckConstraint($"CK_{table}_IconKey", table,
                "\"IconKey\" IN ('flag','web','crypto','pwn','brain','ai')");
        }
    }
}
