using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class VendorPostureExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "sync_posture",
                table: "trendmicro_connections",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "sync_attack_surface_objects",
                table: "trendmicro_connections",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "sync_workbench_alerts",
                table: "trendmicro_connections",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "next_due_at",
                table: "trendmicro_connections",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "history_backfill_days",
                table: "securityscorecard_connections",
                type: "int(11)",
                nullable: false,
                defaultValue: 365);

            migrationBuilder.AddColumn<DateTime>(
                name: "history_backfilled_from",
                table: "securityscorecard_connections",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "store_issue_impact",
                table: "securityscorecard_connections",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "next_due_at",
                table: "securityscorecard_connections",
                type: "datetime",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "score",
                table: "security_scorecard_factors",
                type: "int(11)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int(11)");

            migrationBuilder.AddColumn<bool>(
                name: "is_backfill",
                table: "security_scorecard_factors",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "criticality_source",
                table: "hosts",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "exploit_attempt_count",
                table: "vulnerabilities",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "global_exploit_activity",
                table: "vulnerabilities",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "vendor_assets",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint(20)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    provider = table.Column<int>(type: "int(11)", nullable: false),
                    connection_id = table.Column<int>(type: "int(11)", nullable: false),
                    asset_kind = table.Column<int>(type: "int(11)", nullable: false),
                    external_id = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    criticality = table.Column<int>(type: "int(11)", nullable: true),
                    criticality_raw = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    tags = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    first_seen_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    status = table.Column<int>(type: "int(11)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_vendor_assets_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "security_scorecard_issue_summaries",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    factor_id = table.Column<int>(type: "int(11)", nullable: false),
                    issue_type = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    severity = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    issue_count = table.Column<int>(type: "int(11)", nullable: true),
                    total_score_impact = table.Column<double>(type: "double", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_security_scorecard_issue_summaries_factor_id",
                        column: x => x.factor_id,
                        principalTable: "security_scorecard_factors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.AddColumn<long>(
                name: "vendor_asset_id",
                table: "vendor_scores",
                type: "bigint(20)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_vendor_assets_entity_id",
                table: "vendor_assets",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "uq_vendor_assets_provider_connection_kind_external_id",
                table: "vendor_assets",
                columns: new[] { "provider", "connection_id", "asset_kind", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_security_scorecard_issue_summaries_factor_id",
                table: "security_scorecard_issue_summaries",
                column: "factor_id");

            migrationBuilder.CreateIndex(
                name: "idx_vendor_scores_source_asset",
                table: "vendor_scores",
                columns: new[] { "source", "vendor_asset_id" });

            migrationBuilder.CreateIndex(
                name: "idx_vendor_scores_vendor_asset_id",
                table: "vendor_scores",
                column: "vendor_asset_id");

            migrationBuilder.AddForeignKey(
                name: "fk_vendor_scores_vendor_asset_id",
                table: "vendor_scores",
                column: "vendor_asset_id",
                principalTable: "vendor_assets",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_vendor_scores_vendor_asset_id",
                table: "vendor_scores");

            migrationBuilder.DropIndex(
                name: "idx_vendor_scores_source_asset",
                table: "vendor_scores");

            migrationBuilder.DropIndex(
                name: "idx_vendor_scores_vendor_asset_id",
                table: "vendor_scores");

            migrationBuilder.DropColumn(
                name: "vendor_asset_id",
                table: "vendor_scores");

            migrationBuilder.DropTable(
                name: "security_scorecard_issue_summaries");

            migrationBuilder.DropTable(
                name: "vendor_assets");

            migrationBuilder.DropColumn(name: "global_exploit_activity", table: "vulnerabilities");
            migrationBuilder.DropColumn(name: "exploit_attempt_count", table: "vulnerabilities");
            migrationBuilder.DropColumn(name: "criticality_source", table: "hosts");
            migrationBuilder.DropColumn(name: "is_backfill", table: "security_scorecard_factors");

            migrationBuilder.AlterColumn<int>(
                name: "score",
                table: "security_scorecard_factors",
                type: "int(11)",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldNullable: true);

            migrationBuilder.DropColumn(name: "next_due_at", table: "securityscorecard_connections");
            migrationBuilder.DropColumn(name: "store_issue_impact", table: "securityscorecard_connections");
            migrationBuilder.DropColumn(name: "history_backfilled_from", table: "securityscorecard_connections");
            migrationBuilder.DropColumn(name: "history_backfill_days", table: "securityscorecard_connections");
            migrationBuilder.DropColumn(name: "next_due_at", table: "trendmicro_connections");
            migrationBuilder.DropColumn(name: "sync_workbench_alerts", table: "trendmicro_connections");
            migrationBuilder.DropColumn(name: "sync_attack_surface_objects", table: "trendmicro_connections");
            migrationBuilder.DropColumn(name: "sync_posture", table: "trendmicro_connections");
        }
    }
}
