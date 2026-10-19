using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class RiskIndexSnapshotJobTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "category_state",
                table: "risk_index_snapshots",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "chips",
                table: "risk_index_snapshots",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "display_state",
                table: "risk_index_snapshots",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "markers",
                table: "risk_index_snapshots",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "cleared_r",
                table: "risk_index_object_days",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "coverage_effect",
                table: "risk_index_object_days",
                type: "double",
                nullable: false,
                defaultValue: 1.0);

            migrationBuilder.AddColumn<double>(
                name: "freshness",
                table: "risk_index_object_days",
                type: "double",
                nullable: false,
                defaultValue: 1.0);

            migrationBuilder.AddColumn<double>(
                name: "r_max",
                table: "risk_index_object_days",
                type: "double",
                nullable: false,
                defaultValue: 1.0);

            migrationBuilder.CreateTable(
                name: "risk_index_class_days",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint(20)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    snapshot_date = table.Column<DateOnly>(type: "date", nullable: false),
                    profile_id = table.Column<int>(type: "int(11)", nullable: false),
                    node_entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    tab_group = table.Column<int>(type: "int(11)", nullable: false),
                    category = table.Column<int>(type: "int(11)", nullable: false),
                    value = table.Column<double>(type: "double", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_index_class_days_category", "`category` >= 1 AND `category` <= 6");
                    table.CheckConstraint("ck_risk_index_class_days_tab_group", "`tab_group` >= 1 AND `tab_group` <= 5");
                    table.CheckConstraint("ck_risk_index_class_days_value", "`value` >= 0 AND `value` <= 100");
                    table.ForeignKey(
                        name: "fk_risk_index_class_days_profile_id",
                        column: x => x.profile_id,
                        principalTable: "risk_index_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_index_incident_closures",
                columns: table => new
                {
                    incident_id = table.Column<int>(type: "int(11)", nullable: false),
                    closed_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    frozen_on = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.incident_id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_index_node_members",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint(20)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    snapshot_date = table.Column<DateOnly>(type: "date", nullable: false),
                    profile_id = table.Column<int>(type: "int(11)", nullable: false),
                    node_entity_id = table.Column<int>(type: "int(11)", nullable: false),
                    member_kind = table.Column<int>(type: "int(11)", nullable: false),
                    member_entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_index_node_members_kind", "`member_kind` >= 1 AND `member_kind` <= 2");
                    table.CheckConstraint("ck_risk_index_node_members_member", "`member_kind` = 2 OR `member_entity_id` IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_risk_index_node_members_profile_id",
                        column: x => x.profile_id,
                        principalTable: "risk_index_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_index_object_attributions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint(20)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    snapshot_date = table.Column<DateOnly>(type: "date", nullable: false),
                    profile_id = table.Column<int>(type: "int(11)", nullable: false),
                    object_kind = table.Column<int>(type: "int(11)", nullable: false),
                    object_ref = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    scope_entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_risk_index_object_attributions_profile_id",
                        column: x => x.profile_id,
                        principalTable: "risk_index_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.AddCheckConstraint(
                name: "ck_risk_index_snapshots_category_state",
                table: "risk_index_snapshots",
                sql: "`category_state` IS NULL OR (`category` <> 0 AND `category_state` >= 1 AND `category_state` <= 4)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_risk_index_snapshots_display_state",
                table: "risk_index_snapshots",
                sql: "`display_state` IS NULL OR (`category` = 0 AND `display_state` >= 1 AND `display_state` <= 4)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_risk_index_object_days_engine",
                table: "risk_index_object_days",
                sql: "`freshness` >= 0 AND `freshness` <= 1 AND `coverage_effect` >= 0 AND `coverage_effect` <= 1 AND `r_max` >= 0 AND `cleared_r` >= 0 AND `cleared_r` <= 1");

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_class_days_node_tab_date",
                table: "risk_index_class_days",
                columns: new[] { "node_entity_id", "tab_group", "snapshot_date" });

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_class_days_profile_id",
                table: "risk_index_class_days",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "uq_risk_index_class_days_date_profile_node_tab_category",
                table: "risk_index_class_days",
                columns: new[] { "snapshot_date", "profile_id", "node_entity_id", "tab_group", "category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_node_members_date_profile_node",
                table: "risk_index_node_members",
                columns: new[] { "snapshot_date", "profile_id", "node_entity_id" });

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_node_members_profile_id",
                table: "risk_index_node_members",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_object_attributions_date_profile_entity",
                table: "risk_index_object_attributions",
                columns: new[] { "snapshot_date", "profile_id", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_object_attributions_profile_id",
                table: "risk_index_object_attributions",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_object_attributions_scope_entity_id",
                table: "risk_index_object_attributions",
                column: "scope_entity_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "risk_index_class_days");

            migrationBuilder.DropTable(
                name: "risk_index_incident_closures");

            migrationBuilder.DropTable(
                name: "risk_index_node_members");

            migrationBuilder.DropTable(
                name: "risk_index_object_attributions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_risk_index_snapshots_category_state",
                table: "risk_index_snapshots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_risk_index_snapshots_display_state",
                table: "risk_index_snapshots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_risk_index_object_days_engine",
                table: "risk_index_object_days");

            migrationBuilder.DropColumn(
                name: "category_state",
                table: "risk_index_snapshots");

            migrationBuilder.DropColumn(
                name: "chips",
                table: "risk_index_snapshots");

            migrationBuilder.DropColumn(
                name: "display_state",
                table: "risk_index_snapshots");

            migrationBuilder.DropColumn(
                name: "markers",
                table: "risk_index_snapshots");

            migrationBuilder.DropColumn(
                name: "cleared_r",
                table: "risk_index_object_days");

            migrationBuilder.DropColumn(
                name: "coverage_effect",
                table: "risk_index_object_days");

            migrationBuilder.DropColumn(
                name: "freshness",
                table: "risk_index_object_days");

            migrationBuilder.DropColumn(
                name: "r_max",
                table: "risk_index_object_days");
        }
    }
}
