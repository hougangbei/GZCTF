using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace GZCTF.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261107040000_AddCourseWorkspaceContents")]
public sealed class AddCourseWorkspaceContents : Migration
{
    protected override void Up(MigrationBuilder m) => m.CreateTable("CourseWorkspaceContents", table => new {
        SkillTreeId = table.Column<Guid>(type: "uuid", nullable: false),
        Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
        ContentId = table.Column<Guid>(type: "uuid", nullable: false),
        Version = table.Column<int>(type: "integer", nullable: false),
        PayloadJson = table.Column<string>(type: "text", nullable: false)
    }, constraints: table => {
        table.PrimaryKey("PK_CourseWorkspaceContents", x => new { x.SkillTreeId, x.Kind, x.ContentId });
        table.ForeignKey("FK_CourseWorkspaceContents_CourseAuthoringWorkspaces_SkillTreeId", x => x.SkillTreeId,
            "CourseAuthoringWorkspaces", "SkillTreeId", onDelete: ReferentialAction.Cascade);
    });
    protected override void Down(MigrationBuilder m) => m.DropTable("CourseWorkspaceContents");
}
