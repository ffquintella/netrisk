using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationSyncProgressLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "progress_log",
                table: "integration_sync_logs",
                type: "longtext",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "progress_log",
                table: "integration_sync_logs");
        }
    }
}
