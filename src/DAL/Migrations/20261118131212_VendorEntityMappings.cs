using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class VendorEntityMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "sync_custom_tags",
                table: "trendmicro_connections",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "host_external_ids",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    host_id = table.Column<int>(type: "int(11)", nullable: false),
                    provider = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    connection_id = table.Column<int>(type: "int(11)", nullable: true),
                    external_id = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tags = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    first_seen_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_host_external_ids_host_id",
                        column: x => x.host_id,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "vendor_entity_mappings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    provider = table.Column<int>(type: "int(11)", nullable: false),
                    connection_id = table.Column<int>(type: "int(11)", nullable: false),
                    match_kind = table.Column<int>(type: "int(11)", nullable: false),
                    match_value = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    match_label = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    entity_id = table.Column<int>(type: "int(11)", nullable: false),
                    priority = table.Column<int>(type: "int(11)", nullable: false),
                    restamp_policy = table.Column<int>(type: "int(11)", nullable: false),
                    enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_vendor_entity_mappings_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_vendor_entity_mappings_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_host_external_ids_host_id",
                table: "host_external_ids",
                column: "host_id");

            migrationBuilder.CreateIndex(
                name: "idx_host_external_ids_provider_connection_id",
                table: "host_external_ids",
                columns: new[] { "provider", "connection_id" });

            migrationBuilder.CreateIndex(
                name: "uq_host_external_ids_provider_external_id",
                table: "host_external_ids",
                columns: new[] { "provider", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_vendor_entity_mappings_created_by_id",
                table: "vendor_entity_mappings",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_vendor_entity_mappings_entity_id",
                table: "vendor_entity_mappings",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "uq_vendor_entity_mappings_provider_connection_kind_value",
                table: "vendor_entity_mappings",
                columns: new[] { "provider", "connection_id", "match_kind", "match_value" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "host_external_ids");

            migrationBuilder.DropTable(
                name: "vendor_entity_mappings");

            migrationBuilder.DropColumn(
                name: "sync_custom_tags",
                table: "trendmicro_connections");
        }
    }
}
