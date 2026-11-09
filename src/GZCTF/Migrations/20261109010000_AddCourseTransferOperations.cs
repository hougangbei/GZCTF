using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace GZCTF.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261109010000_AddCourseTransferOperations")]
public sealed class AddCourseTransferOperations : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>("DetailsJson", "AuditEvents", type: "text", nullable: true);
        m.CreateTable("CourseTransferOperations", table => new {
        ActorId = table.Column<Guid>(type: "uuid", nullable: false),
        OperationId = table.Column<Guid>(type: "uuid", nullable: false),
        RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
        ResponseJson = table.Column<string>(type: "text", nullable: false)
    }, constraints: table => table.PrimaryKey("PK_CourseTransferOperations", x => new { x.ActorId, x.OperationId }));
    }
    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("CourseTransferOperations");
        m.DropColumn("DetailsJson", "AuditEvents");
    }
}
