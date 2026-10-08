using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track9AiGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_models",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    purpose = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    kind = table.Column<int>(type: "int(11)", nullable: false),
                    source = table.Column<int>(type: "int(11)", nullable: false),
                    third_party_id = table.Column<int>(type: "int(11)", nullable: true),
                    version = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    version_since = table.Column<DateTime>(type: "datetime", nullable: true),
                    status = table.Column<int>(type: "int(11)", nullable: false),
                    risk_tier = table.Column<int>(type: "int(11)", nullable: true),
                    human_oversight = table.Column<int>(type: "int(11)", nullable: true),
                    owner_id = table.Column<int>(type: "int(11)", nullable: true),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    max_evaluation_age_days = table.Column<int>(type: "int(11)", nullable: false),
                    data_declared_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    notes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    retired_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    retired_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    retire_reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    updated_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_ai_models_human_oversight", "`human_oversight` IS NULL OR (`human_oversight` >= 1 AND `human_oversight` <= 3)");
                    table.CheckConstraint("ck_ai_models_kind", "`kind` >= 1 AND `kind` <= 7");
                    table.CheckConstraint("ck_ai_models_max_evaluation_age_days", "`max_evaluation_age_days` >= 1 AND `max_evaluation_age_days` <= 1096");
                    table.CheckConstraint("ck_ai_models_retired", "(`status` = 4 AND `retired_at` IS NOT NULL AND `retire_reason` IS NOT NULL) OR (`status` <> 4 AND `retired_at` IS NULL)");
                    table.CheckConstraint("ck_ai_models_risk_tier", "`risk_tier` IS NULL OR (`risk_tier` >= 1 AND `risk_tier` <= 3)");
                    table.CheckConstraint("ck_ai_models_source", "`source` >= 1 AND `source` <= 3");
                    table.CheckConstraint("ck_ai_models_status", "`status` >= 1 AND `status` <= 4");
                    table.ForeignKey(
                        name: "fk_ai_models_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ai_models_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ai_models_owner_id",
                        column: x => x.owner_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ai_models_retired_by_id",
                        column: x => x.retired_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ai_models_third_party_id",
                        column: x => x.third_party_id,
                        principalTable: "third_parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_models_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "ai_model_data_links",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    model_id = table.Column<int>(type: "int(11)", nullable: false),
                    entity_id = table.Column<int>(type: "int(11)", nullable: false),
                    data_usage = table.Column<int>(type: "int(11)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_ai_model_data_links_data_usage", "`data_usage` >= 1 AND `data_usage` <= 5");
                    table.ForeignKey(
                        name: "fk_ai_model_data_links_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ai_model_data_links_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ai_model_data_links_model_id",
                        column: x => x.model_id,
                        principalTable: "ai_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "ai_model_metric_readings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    model_id = table.Column<int>(type: "int(11)", nullable: false),
                    metric = table.Column<int>(type: "int(11)", nullable: false),
                    value = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    model_version = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    measured_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    period_start = table.Column<DateTime>(type: "datetime", nullable: true),
                    period_end = table.Column<DateTime>(type: "datetime", nullable: true),
                    sample_size = table.Column<int>(type: "int(11)", nullable: true),
                    override_count = table.Column<int>(type: "int(11)", nullable: true),
                    method = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    evidence_reference = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recorded_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    voided_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    voided_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    void_reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_ai_model_metric_readings_metric", "`metric` >= 1 AND `metric` <= 6");
                    table.CheckConstraint("ck_ai_model_metric_readings_override_rate", "(`metric` = 6 AND `period_start` IS NOT NULL AND `sample_size` IS NOT NULL AND `override_count` IS NOT NULL AND `override_count` >= 0 AND `override_count` <= `sample_size`) OR (`metric` <> 6 AND `override_count` IS NULL)");
                    table.CheckConstraint("ck_ai_model_metric_readings_period", "(`period_start` IS NULL AND `period_end` IS NULL) OR (`period_start` IS NOT NULL AND `period_end` IS NOT NULL AND `period_end` > `period_start`)");
                    table.CheckConstraint("ck_ai_model_metric_readings_sample_size", "`sample_size` IS NULL OR `sample_size` >= 1");
                    table.CheckConstraint("ck_ai_model_metric_readings_value", "`value` >= 0 AND (`metric` = 5 OR `value` <= 1)");
                    table.CheckConstraint("ck_ai_model_metric_readings_void", "(`voided_at` IS NULL AND `void_reason` IS NULL) OR (`voided_at` IS NOT NULL AND `void_reason` IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_ai_model_metric_readings_model_id",
                        column: x => x.model_id,
                        principalTable: "ai_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_model_metric_readings_recorded_by_id",
                        column: x => x.recorded_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ai_model_metric_readings_voided_by_id",
                        column: x => x.voided_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "ai_model_overrides",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    model_id = table.Column<int>(type: "int(11)", nullable: false),
                    model_version = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    occurred_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    model_output = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    human_decision = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recorded_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    voided_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    voided_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    void_reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_ai_model_overrides_void", "(`voided_at` IS NULL AND `void_reason` IS NULL) OR (`voided_at` IS NOT NULL AND `void_reason` IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_ai_model_overrides_model_id",
                        column: x => x.model_id,
                        principalTable: "ai_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_model_overrides_recorded_by_id",
                        column: x => x.recorded_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ai_model_overrides_voided_by_id",
                        column: x => x.voided_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "ai_model_risks",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    model_id = table.Column<int>(type: "int(11)", nullable: false),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_model_risks_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ai_model_risks_model_id",
                        column: x => x.model_id,
                        principalTable: "ai_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_model_risks_risk_id",
                        column: x => x.risk_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_ai_model_data_links_created_by_id",
                table: "ai_model_data_links",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_ai_model_data_links_entity_id",
                table: "ai_model_data_links",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "uq_ai_model_data_links_model_id_entity_id_data_usage",
                table: "ai_model_data_links",
                columns: new[] { "model_id", "entity_id", "data_usage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_ai_model_metric_readings_model_id_metric_measured_at",
                table: "ai_model_metric_readings",
                columns: new[] { "model_id", "metric", "measured_at" });

            migrationBuilder.CreateIndex(
                name: "idx_ai_model_metric_readings_recorded_by_id",
                table: "ai_model_metric_readings",
                column: "recorded_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_ai_model_metric_readings_voided_by_id",
                table: "ai_model_metric_readings",
                column: "voided_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_ai_model_overrides_model_id_occurred_at",
                table: "ai_model_overrides",
                columns: new[] { "model_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "idx_ai_model_overrides_recorded_by_id",
                table: "ai_model_overrides",
                column: "recorded_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_ai_model_overrides_voided_by_id",
                table: "ai_model_overrides",
                column: "voided_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_ai_model_risks_created_by_id",
                table: "ai_model_risks",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_ai_model_risks_risk_id",
                table: "ai_model_risks",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "uq_ai_model_risks_model_id_risk_id",
                table: "ai_model_risks",
                columns: new[] { "model_id", "risk_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_ai_models_created_by_id",
                table: "ai_models",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_ai_models_entity_id",
                table: "ai_models",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_ai_models_owner_id",
                table: "ai_models",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "idx_ai_models_retired_by_id",
                table: "ai_models",
                column: "retired_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_ai_models_status",
                table: "ai_models",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "idx_ai_models_third_party_id",
                table: "ai_models",
                column: "third_party_id");

            migrationBuilder.CreateIndex(
                name: "idx_ai_models_updated_by_id",
                table: "ai_models",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_ai_models_name",
                table: "ai_models",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_model_data_links");

            migrationBuilder.DropTable(
                name: "ai_model_metric_readings");

            migrationBuilder.DropTable(
                name: "ai_model_overrides");

            migrationBuilder.DropTable(
                name: "ai_model_risks");

            migrationBuilder.DropTable(
                name: "ai_models");
        }
    }
}
