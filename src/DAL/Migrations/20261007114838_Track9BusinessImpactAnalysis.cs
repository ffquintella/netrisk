using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track9BusinessImpactAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bia_dependencies",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    dependent_entity_id = table.Column<int>(type: "int(11)", nullable: false),
                    provider_entity_id = table.Column<int>(type: "int(11)", nullable: false),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_bia_dependencies_not_self", "`dependent_entity_id` <> `provider_entity_id`");
                    table.ForeignKey(
                        name: "fk_bia_dependencies_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_bia_dependencies_dependent_entity_id",
                        column: x => x.dependent_entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bia_dependencies_provider_entity_id",
                        column: x => x.provider_entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "business_impact_analyses",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    entity_id = table.Column<int>(type: "int(11)", nullable: false),
                    mtpd_minutes = table.Column<int>(type: "int(11)", nullable: true),
                    rto_minutes = table.Column<int>(type: "int(11)", nullable: true),
                    rpo_minutes = table.Column<int>(type: "int(11)", nullable: true),
                    assessed_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    updated_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_business_impact_analyses_declared", "`mtpd_minutes` IS NOT NULL OR `rto_minutes` IS NOT NULL OR `rpo_minutes` IS NOT NULL");
                    table.CheckConstraint("ck_business_impact_analyses_non_negative", "(`mtpd_minutes` IS NULL OR `mtpd_minutes` >= 0) AND (`rto_minutes` IS NULL OR `rto_minutes` >= 0) AND (`rpo_minutes` IS NULL OR `rpo_minutes` >= 0)");
                    table.CheckConstraint("ck_business_impact_analyses_rto_within_mtpd", "`rto_minutes` IS NULL OR `mtpd_minutes` IS NULL OR `rto_minutes` <= `mtpd_minutes`");
                    table.ForeignKey(
                        name: "fk_business_impact_analyses_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_business_impact_analyses_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_business_impact_analyses_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "restoration_tests",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    entity_id = table.Column<int>(type: "int(11)", nullable: false),
                    tested_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    outcome = table.Column<int>(type: "int(11)", nullable: false),
                    achieved_rto_minutes = table.Column<int>(type: "int(11)", nullable: true),
                    achieved_rpo_minutes = table.Column<int>(type: "int(11)", nullable: true),
                    declared_rto_minutes = table.Column<int>(type: "int(11)", nullable: true),
                    declared_rpo_minutes = table.Column<int>(type: "int(11)", nullable: true),
                    evidence_reference = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    notes = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    recorded_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    voided_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    voided_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    void_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_restoration_tests_measured", "`outcome` <> 1 OR `achieved_rto_minutes` IS NOT NULL OR `achieved_rpo_minutes` IS NOT NULL");
                    table.CheckConstraint("ck_restoration_tests_non_negative", "(`achieved_rto_minutes` IS NULL OR `achieved_rto_minutes` >= 0) AND (`achieved_rpo_minutes` IS NULL OR `achieved_rpo_minutes` >= 0)");
                    table.CheckConstraint("ck_restoration_tests_void_complete", "(`voided_at` IS NULL) = (`void_reason` IS NULL)");
                    table.ForeignKey(
                        name: "fk_restoration_tests_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_restoration_tests_recorded_by_id",
                        column: x => x.recorded_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_restoration_tests_voided_by_id",
                        column: x => x.voided_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_bia_dependencies_created_by_id",
                table: "bia_dependencies",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_bia_dependencies_provider_entity_id",
                table: "bia_dependencies",
                column: "provider_entity_id");

            migrationBuilder.CreateIndex(
                name: "uq_bia_dependencies_dependent_entity_id_provider_entity_id",
                table: "bia_dependencies",
                columns: new[] { "dependent_entity_id", "provider_entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_business_impact_analyses_created_by_id",
                table: "business_impact_analyses",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_business_impact_analyses_updated_by_id",
                table: "business_impact_analyses",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_business_impact_analyses_entity_id",
                table: "business_impact_analyses",
                column: "entity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_restoration_tests_entity_id_tested_at",
                table: "restoration_tests",
                columns: new[] { "entity_id", "tested_at" });

            migrationBuilder.CreateIndex(
                name: "idx_restoration_tests_recorded_by_id",
                table: "restoration_tests",
                column: "recorded_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_restoration_tests_voided_by_id",
                table: "restoration_tests",
                column: "voided_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bia_dependencies");

            migrationBuilder.DropTable(
                name: "business_impact_analyses");

            migrationBuilder.DropTable(
                name: "restoration_tests");
        }
    }
}
