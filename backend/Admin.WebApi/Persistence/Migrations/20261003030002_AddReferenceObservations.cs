using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Admin.WebApi.Persistence.Migrations;

[DbContext(typeof(AuditDbContext))]
[Migration("20261003030002_AddReferenceObservations")]
public sealed class AddReferenceObservations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("ReferenceContractVersion", "OssAuditRuns", type: "text", nullable: true);
        migrationBuilder.AddColumn<string>("ReferenceSnapshots", "OssAuditRuns", type: "text", nullable: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $body$ BEGIN
              IF EXISTS(SELECT 1 FROM "OssAuditRuns" WHERE "ReferenceSnapshots" IS NOT NULL)
                 OR EXISTS(SELECT 1 FROM "OssAuditRecords" WHERE "Status"=3) THEN
                RAISE EXCEPTION 'nonempty_reference_observation_downgrade';
              END IF;
            END $body$;
            """);
        migrationBuilder.DropColumn("ReferenceSnapshots", "OssAuditRuns");
        migrationBuilder.DropColumn("ReferenceContractVersion", "OssAuditRuns");
    }
}
