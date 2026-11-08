using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace GZCTF.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261107050000_AddPrivateCourseCovers")]
public sealed class AddPrivateCourseCovers : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>("CoverStorageKey", "CourseAuthoringWorkspaces", type: "text", nullable: true);
        m.AddColumn<string>("CoverStorageKey", "CoursePublishedVersions", type: "text", nullable: true);
    }
    protected override void Down(MigrationBuilder m)
    {
        m.DropColumn("CoverStorageKey", "CourseAuthoringWorkspaces");
        m.DropColumn("CoverStorageKey", "CoursePublishedVersions");
    }
}
