using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace GZCTF.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261107010000_AddCourseAuthoringWorkspace")]
public sealed class AddCourseAuthoringWorkspace : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("CourseAuthoringWorkspaces", table => new
        {
            SkillTreeId = table.Column<Guid>(type: "uuid", nullable: false),
            Version = table.Column<Guid>(type: "uuid", nullable: false),
            DocumentJson = table.Column<string>(type: "text", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_CourseAuthoringWorkspaces", x => x.SkillTreeId);
            table.ForeignKey("FK_CourseAuthoringWorkspaces_SkillTrees_SkillTreeId", x => x.SkillTreeId,
                "SkillTrees", "Id", onDelete: ReferentialAction.Cascade);
        });
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("CourseAuthoringWorkspaces");
}
