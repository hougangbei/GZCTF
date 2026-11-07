using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace GZCTF.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261107070000_TrackCourseStoredAssets")]
public sealed class TrackCourseStoredAssets : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("CourseStoredAssets", table => new {
            StorageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
            ReferenceToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
            LastTouchedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_CourseStoredAssets", x => x.StorageKey));
        m.CreateIndex("IX_CourseStoredAssets_LastTouchedUtc", "CourseStoredAssets", "LastTouchedUtc");
    }
    protected override void Down(MigrationBuilder m) => m.DropTable("CourseStoredAssets");
}
