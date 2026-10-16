using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class RiskIndexTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "risk_index_profiles",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    version = table.Column<int>(type: "int(11)", nullable: false),
                    name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    preset = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mode = table.Column<int>(type: "int(11)", nullable: false),
                    status = table.Column<int>(type: "int(11)", nullable: false),
                    parameters = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    justification = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sensitivity_report = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    committee_decision_ref = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    board_approval_ref = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_rebaseline = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    rejected_reason = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    approved_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    approved_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    approval_grant_source = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    effective_from = table.Column<DateTime>(type: "datetime", nullable: true),
                    retired_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    retired_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    retire_justification = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    retire_decision_ref = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    revision = table.Column<int>(type: "int(11)", nullable: false, defaultValue: 0),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_index_profiles_approved", "`status` NOT IN (3, 4) OR (`approved_at` IS NOT NULL AND `effective_from` IS NOT NULL AND `committee_decision_ref` IS NOT NULL)");
                    table.CheckConstraint("ck_risk_index_profiles_mode", "`mode` >= 1 AND `mode` <= 2");
                    table.CheckConstraint("ck_risk_index_profiles_rejected", "`status` <> 5 OR `rejected_reason` IS NOT NULL");
                    table.CheckConstraint("ck_risk_index_profiles_retired", "(`status` = 4 AND `retired_at` IS NOT NULL) OR (`status` <> 4 AND `retired_at` IS NULL)");
                    table.CheckConstraint("ck_risk_index_profiles_status", "`status` >= 1 AND `status` <= 5");
                    table.CheckConstraint("ck_risk_index_profiles_submitted", "`status` = 1 OR (`submitted_at` IS NOT NULL AND `justification` IS NOT NULL)");
                    table.CheckConstraint("ck_risk_index_profiles_version", "`version` >= 1");
                    table.ForeignKey(
                        name: "fk_risk_index_profiles_approved_by_id",
                        column: x => x.approved_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_index_profiles_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_index_profiles_retired_by_id",
                        column: x => x.retired_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_index_object_days",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint(20)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    snapshot_date = table.Column<DateOnly>(type: "date", nullable: false),
                    profile_id = table.Column<int>(type: "int(11)", nullable: false),
                    object_kind = table.Column<int>(type: "int(11)", nullable: false),
                    object_ref = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    category = table.Column<int>(type: "int(11)", nullable: false),
                    population = table.Column<int>(type: "int(11)", nullable: false),
                    r_value = table.Column<double>(type: "double", nullable: false),
                    weight = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    criticality = table.Column<sbyte>(type: "tinyint(4)", nullable: false),
                    criticality_source = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    criticality_author_id = table.Column<int>(type: "int(11)", nullable: true),
                    state = table.Column<int>(type: "int(11)", nullable: false),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    attribution = table.Column<string>(type: "text", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    factors = table.Column<string>(type: "text", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    scenario_inputs = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    signals = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    asset_class = table.Column<int>(type: "int(11)", nullable: true),
                    source_mask = table.Column<int>(type: "int(11)", nullable: false, defaultValue: 0),
                    environment = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_index_object_days_category", "`category` >= 1 AND `category` <= 6");
                    table.CheckConstraint("ck_risk_index_object_days_criticality", "`criticality` >= 1 AND `criticality` <= 5");
                    table.CheckConstraint("ck_risk_index_object_days_r_value", "`r_value` >= 0 AND `r_value` <= 1");
                    table.CheckConstraint("ck_risk_index_object_days_weight", "`weight` >= 0");
                    table.ForeignKey(
                        name: "fk_risk_index_object_days_profile_id",
                        column: x => x.profile_id,
                        principalTable: "risk_index_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_index_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint(20)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    snapshot_date = table.Column<DateOnly>(type: "date", nullable: false),
                    profile_id = table.Column<int>(type: "int(11)", nullable: false),
                    mode = table.Column<int>(type: "int(11)", nullable: false),
                    scope_kind = table.Column<int>(type: "int(11)", nullable: false),
                    scope_ref = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    category = table.Column<int>(type: "int(11)", nullable: false),
                    value_unfloored = table.Column<double>(type: "double", nullable: false),
                    value_floored = table.Column<double>(type: "double", nullable: false),
                    value_displayed = table.Column<int>(type: "int(11)", nullable: false),
                    floor_reasons = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    coverage = table.Column<decimal>(type: "decimal(4,3)", precision: 4, scale: 3, nullable: false),
                    freshness = table.Column<decimal>(type: "decimal(4,3)", precision: 4, scale: 3, nullable: false),
                    quality = table.Column<decimal>(type: "decimal(4,3)", precision: 4, scale: 3, nullable: false),
                    interval_low = table.Column<double>(type: "double", nullable: true),
                    interval_high = table.Column<double>(type: "double", nullable: true),
                    object_count = table.Column<int>(type: "int(11)", nullable: false),
                    presumption_points = table.Column<double>(type: "double", nullable: false),
                    external_inputs_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    top_contributions = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    change_attribution = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_index_snapshots_category", "`category` >= 0 AND `category` <= 6");
                    table.CheckConstraint("ck_risk_index_snapshots_fractions", "`coverage` >= 0 AND `coverage` <= 1 AND `freshness` >= 0 AND `freshness` <= 1 AND `quality` >= 0 AND `quality` <= 1");
                    table.CheckConstraint("ck_risk_index_snapshots_interval", "(`interval_low` IS NULL AND `interval_high` IS NULL) OR (`interval_low` IS NOT NULL AND `interval_high` IS NOT NULL AND `interval_low` <= `interval_high`)");
                    table.CheckConstraint("ck_risk_index_snapshots_mode", "`mode` >= 1 AND `mode` <= 2");
                    table.CheckConstraint("ck_risk_index_snapshots_scope_kind", "`scope_kind` >= 1 AND `scope_kind` <= 4");
                    table.CheckConstraint("ck_risk_index_snapshots_value_displayed", "`value_displayed` >= 0 AND `value_displayed` <= 100");
                    table.ForeignKey(
                        name: "fk_risk_index_snapshots_profile_id",
                        column: x => x.profile_id,
                        principalTable: "risk_index_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_object_days_date_class",
                table: "risk_index_object_days",
                columns: new[] { "snapshot_date", "asset_class" });

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_object_days_date_profile_category",
                table: "risk_index_object_days",
                columns: new[] { "snapshot_date", "profile_id", "category" });

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_object_days_entity_id",
                table: "risk_index_object_days",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_object_days_profile_id",
                table: "risk_index_object_days",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "uq_risk_index_object_days_date_profile_object_category",
                table: "risk_index_object_days",
                columns: new[] { "snapshot_date", "profile_id", "object_kind", "object_ref", "category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_profiles_approved_by_id",
                table: "risk_index_profiles",
                column: "approved_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_profiles_created_by_id",
                table: "risk_index_profiles",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_profiles_retired_by_id",
                table: "risk_index_profiles",
                column: "retired_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_profiles_status",
                table: "risk_index_profiles",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "uq_risk_index_profiles_version",
                table: "risk_index_profiles",
                column: "version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_snapshots_entity_id",
                table: "risk_index_snapshots",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_snapshots_profile_id",
                table: "risk_index_snapshots",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_snapshots_scope_category_date",
                table: "risk_index_snapshots",
                columns: new[] { "scope_kind", "scope_ref", "category", "snapshot_date" });

            migrationBuilder.CreateIndex(
                name: "uq_risk_index_snapshots_date_profile_scope_category",
                table: "risk_index_snapshots",
                columns: new[] { "snapshot_date", "profile_id", "scope_kind", "scope_ref", "category" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "risk_index_object_days");

            migrationBuilder.DropTable(
                name: "risk_index_snapshots");

            migrationBuilder.DropTable(
                name: "risk_index_profiles");
        }
    }
}
