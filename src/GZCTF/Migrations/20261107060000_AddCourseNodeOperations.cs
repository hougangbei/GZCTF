using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace GZCTF.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261107060000_AddCourseNodeOperations")]
public sealed class AddCourseNodeOperations : Migration
{
    protected override void Up(MigrationBuilder m) => m.CreateTable("CourseNodeOperations", table => new {
        SkillTreeId = table.Column<Guid>(type: "uuid", nullable: false),
        OperationId = table.Column<Guid>(type: "uuid", nullable: false),
        RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
        ResponseJson = table.Column<string>(type: "text", nullable: false)
    }, constraints: table => {
        table.PrimaryKey("PK_CourseNodeOperations", x => new { x.SkillTreeId, x.OperationId });
        table.ForeignKey("FK_CourseNodeOperations_CourseAuthoringWorkspaces_SkillTreeId", x => x.SkillTreeId, "CourseAuthoringWorkspaces", "SkillTreeId", onDelete: ReferentialAction.Cascade);
    });
    protected override void Down(MigrationBuilder m) => m.DropTable("CourseNodeOperations");
}
