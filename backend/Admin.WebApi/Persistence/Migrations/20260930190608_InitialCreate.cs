using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Admin.WebApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OssAuditRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    ObjectPath = table.Column<string>(type: "text", nullable: false),
                    Bucket = table.Column<string>(type: "text", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    LastModified = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    ResolvedAt = table.Column<long>(type: "bigint", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("OssAuditRecords_pkey", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OssAuditRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    StartedAt = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAt = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    NewZombieCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TriggerType = table.Column<string>(type: "text", nullable: false, defaultValue: "scheduled"),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("OssAuditRuns_pkey", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OssAuditRecords_Bucket",
                table: "OssAuditRecords",
                column: "Bucket");

            migrationBuilder.CreateIndex(
                name: "IX_OssAuditRecords_CreatedAt",
                table: "OssAuditRecords",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OssAuditRecords_ObjectPath",
                table: "OssAuditRecords",
                column: "ObjectPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OssAuditRecords_Status",
                table: "OssAuditRecords",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OssAuditRuns_StartedAt",
                table: "OssAuditRuns",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OssAuditRecords");

            migrationBuilder.DropTable(
                name: "OssAuditRuns");
        }
    }
}
