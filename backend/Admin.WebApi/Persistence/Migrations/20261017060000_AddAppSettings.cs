using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Admin.WebApi.Persistence.Migrations;

[DbContext(typeof(AuditDbContext))]
[Migration("20261017060000_AddAppSettings")]
public sealed class AddAppSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Forward-only chain: no Down is implemented (rolling back means restoring the
        // whole database, per docs/database/migrations.md).
        migrationBuilder.CreateTable(
            name: "AppSettings",
            columns: table => new
            {
                Key = table.Column<string>(type: "text", nullable: false),
                Value = table.Column<string>(type: "text", nullable: false),
                UpdatedAt = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                UpdatedBy = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("AppSettings_pkey", x => x.Key);
            });
    }
}
