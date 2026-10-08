using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track9KriReassessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "kris",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    category = table.Column<int>(type: "int(11)", nullable: false),
                    source = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    unit = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    direction = table.Column<int>(type: "int(11)", nullable: false),
                    tolerance_threshold = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    warning_threshold = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    tolerance_rationale = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    max_reading_age_days = table.Column<int>(type: "int(11)", nullable: false),
                    owner_id = table.Column<int>(type: "int(11)", nullable: true),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    retired_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    updated_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_kris_category", "`category` >= 1 AND `category` <= 4");
                    table.CheckConstraint("ck_kris_direction", "`direction` >= 1 AND `direction` <= 2");
                    table.CheckConstraint("ck_kris_max_reading_age_days", "`max_reading_age_days` >= 1 AND `max_reading_age_days` <= 366");
                    table.CheckConstraint("ck_kris_warning_side", "`warning_threshold` IS NULL OR (`direction` = 1 AND `warning_threshold` < `tolerance_threshold`) OR (`direction` = 2 AND `warning_threshold` > `tolerance_threshold`)");
                    table.ForeignKey(
                        name: "fk_kris_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_kris_owner_id",
                        column: x => x.owner_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_kris_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "kri_readings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    kri_id = table.Column<int>(type: "int(11)", nullable: false),
                    value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    observed_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    note = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci")
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
                    table.CheckConstraint("ck_kri_readings_void", "(`voided_at` IS NULL AND `void_reason` IS NULL) OR (`voided_at` IS NOT NULL AND `void_reason` IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_kri_readings_kri_id",
                        column: x => x.kri_id,
                        principalTable: "kris",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_kri_readings_recorded_by_id",
                        column: x => x.recorded_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_kri_readings_voided_by_id",
                        column: x => x.voided_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "kri_risks",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    kri_id = table.Column<int>(type: "int(11)", nullable: false),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_kri_risks_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_kri_risks_kri_id",
                        column: x => x.kri_id,
                        principalTable: "kris",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_kri_risks_risk_id",
                        column: x => x.risk_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "reassessment_events",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    trigger_type = table.Column<int>(type: "int(11)", nullable: false),
                    origin = table.Column<int>(type: "int(11)", nullable: false),
                    title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    occurred_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    incident_id = table.Column<int>(type: "int(11)", nullable: true),
                    kri_id = table.Column<int>(type: "int(11)", nullable: true),
                    kri_reading_id = table.Column<int>(type: "int(11)", nullable: true),
                    kri_breach_ended_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    declared_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_reassessment_events_incident_type", "`incident_id` IS NULL OR `trigger_type` = 3");
                    table.CheckConstraint("ck_reassessment_events_kri_origin", "(`origin` = 2 AND `kri_id` IS NOT NULL AND `kri_reading_id` IS NOT NULL AND `trigger_type` = 6) OR (`origin` = 1 AND `kri_id` IS NULL AND `kri_reading_id` IS NULL AND `kri_breach_ended_at` IS NULL)");
                    table.CheckConstraint("ck_reassessment_events_origin", "`origin` >= 1 AND `origin` <= 2");
                    table.CheckConstraint("ck_reassessment_events_trigger_type", "`trigger_type` >= 1 AND `trigger_type` <= 6");
                    table.ForeignKey(
                        name: "fk_reassessment_events_declared_by_id",
                        column: x => x.declared_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_reassessment_events_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_reassessment_events_kri_id",
                        column: x => x.kri_id,
                        principalTable: "kris",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reassessment_events_kri_reading_id",
                        column: x => x.kri_reading_id,
                        principalTable: "kri_readings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_reassessment_triggers",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    event_id = table.Column<int>(type: "int(11)", nullable: false),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    raised_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_risk_reassessment_triggers_event_id",
                        column: x => x.event_id,
                        principalTable: "reassessment_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_risk_reassessment_triggers_risk_id",
                        column: x => x.risk_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_kri_readings_kri_id_observed_at",
                table: "kri_readings",
                columns: new[] { "kri_id", "observed_at" });

            migrationBuilder.CreateIndex(
                name: "idx_kri_readings_recorded_by_id",
                table: "kri_readings",
                column: "recorded_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_kri_readings_voided_by_id",
                table: "kri_readings",
                column: "voided_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_kri_risks_created_by_id",
                table: "kri_risks",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_kri_risks_risk_id",
                table: "kri_risks",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "uq_kri_risks_kri_id_risk_id",
                table: "kri_risks",
                columns: new[] { "kri_id", "risk_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_kris_entity_id",
                table: "kris",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_kris_owner_id",
                table: "kris",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "idx_kris_updated_by_id",
                table: "kris",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_reassessment_events_declared_by_id",
                table: "reassessment_events",
                column: "declared_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_reassessment_events_kri_id",
                table: "reassessment_events",
                column: "kri_id");

            migrationBuilder.CreateIndex(
                name: "uq_reassessment_events_incident_id",
                table: "reassessment_events",
                column: "incident_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_reassessment_events_kri_reading_id",
                table: "reassessment_events",
                column: "kri_reading_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_risk_reassessment_triggers_risk_id",
                table: "risk_reassessment_triggers",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "uq_risk_reassessment_triggers_event_id_risk_id",
                table: "risk_reassessment_triggers",
                columns: new[] { "event_id", "risk_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kri_risks");

            migrationBuilder.DropTable(
                name: "risk_reassessment_triggers");

            migrationBuilder.DropTable(
                name: "reassessment_events");

            migrationBuilder.DropTable(
                name: "kri_readings");

            migrationBuilder.DropTable(
                name: "kris");
        }
    }
}
