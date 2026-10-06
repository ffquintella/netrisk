using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track9RiskChainLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "risk_chain_links",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    chain_level = table.Column<int>(type: "int(11)", nullable: false),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    host_id = table.Column<int>(type: "int(11)", nullable: true),
                    origin = table.Column<int>(type: "int(11)", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_chain_links_one_target", "((`entity_id` IS NOT NULL) + (`host_id` IS NOT NULL)) = 1");
                    table.ForeignKey(
                        name: "fk_risk_chain_links_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_chain_links_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_risk_chain_links_host_id",
                        column: x => x.host_id,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_risk_chain_links_risk_id",
                        column: x => x.risk_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_risk_chain_links_created_by_id",
                table: "risk_chain_links",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_chain_links_entity_id",
                table: "risk_chain_links",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_chain_links_host_id",
                table: "risk_chain_links",
                column: "host_id");

            migrationBuilder.CreateIndex(
                name: "uq_risk_chain_links_risk_id_entity_id",
                table: "risk_chain_links",
                columns: new[] { "risk_id", "entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_risk_chain_links_risk_id_host_id",
                table: "risk_chain_links",
                columns: new[] { "risk_id", "host_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "risk_chain_links");
        }
    }
}
