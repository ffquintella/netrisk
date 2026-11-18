using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class TenableIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "heartbeat_at",
                table: "integration_sync_logs",
                type: "datetime",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tenable_connections",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    product = table.Column<int>(type: "int(11)", nullable: false),
                    base_url = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    encrypted_access_key = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    encrypted_secret_key = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    sync_interval_hours = table.Column<int>(type: "int(11)", nullable: false),
                    sync_findings = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    sync_assets = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    collect_acr = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    collect_aes = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    min_severity = table.Column<int>(type: "int(11)", nullable: false),
                    initial_window_days = table.Column<int>(type: "int(11)", nullable: false),
                    include_unlicensed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    allow_invalid_certificate = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    findings_cursor = table.Column<DateTime>(type: "datetime", nullable: true),
                    assets_cursor = table.Column<DateTime>(type: "datetime", nullable: true),
                    next_due_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    last_sync_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    last_sync_status = table.Column<int>(type: "int(11)", nullable: true),
                    last_sync_error = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenable_connections_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "vendor_scores",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint(20)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    source = table.Column<int>(type: "int(11)", nullable: false),
                    connection_id = table.Column<int>(type: "int(11)", nullable: false),
                    object_kind = table.Column<int>(type: "int(11)", nullable: false),
                    host_id = table.Column<int>(type: "int(11)", nullable: true),
                    external_ref = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    value = table.Column<double>(type: "double", nullable: false),
                    scale_max = table.Column<double>(type: "double", nullable: false),
                    level = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    details = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    first_captured_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    last_captured_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_vendor_scores_host_id",
                        column: x => x.host_id,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_tenable_connections_entity_id",
                table: "tenable_connections",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "uq_tenable_connections_name",
                table: "tenable_connections",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_vendor_scores_entity_id",
                table: "vendor_scores",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_vendor_scores_host_id",
                table: "vendor_scores",
                column: "host_id");

            migrationBuilder.CreateIndex(
                name: "idx_vendor_scores_source_host",
                table: "vendor_scores",
                columns: new[] { "source", "host_id" });

            migrationBuilder.CreateIndex(
                name: "idx_vendor_scores_source_ref",
                table: "vendor_scores",
                columns: new[] { "source", "external_ref" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenable_connections");

            migrationBuilder.DropTable(
                name: "vendor_scores");

            migrationBuilder.DropColumn(
                name: "heartbeat_at",
                table: "integration_sync_logs");
        }
    }
}
