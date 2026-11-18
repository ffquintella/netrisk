using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class CmdbExposureAndApplicationLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "internet_facing",
                table: "hosts",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "internet_facing_seen_at",
                table: "hosts",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "internet_facing_source",
                table: "hosts",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "host_application_links",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    host_id = table.Column<int>(type: "int(11)", nullable: false),
                    entity_id = table.Column<int>(type: "int(11)", nullable: false),
                    source = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    connection_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    last_seen_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_host_application_links_connection_id",
                        column: x => x.connection_id,
                        principalTable: "issue_tracker_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_host_application_links_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_host_application_links_host_id",
                        column: x => x.host_id,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_host_application_links_connection_id",
                table: "host_application_links",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "idx_host_application_links_entity_id",
                table: "host_application_links",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "uq_host_application_links_host_id_entity_id",
                table: "host_application_links",
                columns: new[] { "host_id", "entity_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "host_application_links");

            migrationBuilder.DropColumn(
                name: "internet_facing",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "internet_facing_seen_at",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "internet_facing_source",
                table: "hosts");
        }
    }
}
