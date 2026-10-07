using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track9TreatmentEconomics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "quant_residual_ale_mean",
                table: "risk_scoring",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "acceptance_criterion",
                table: "mitigation_tasks",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "completion_evidence",
                table: "mitigation_tasks",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "completion_evidence_at",
                table: "mitigation_tasks",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "completion_evidence_by_id",
                table: "mitigation_tasks",
                type: "int(11)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "mitigation_dependencies",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    mitigation_id = table.Column<int>(type: "int(11)", nullable: false),
                    prerequisite_id = table.Column<int>(type: "int(11)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_mitigation_dependencies_not_self", "`mitigation_id` <> `prerequisite_id`");
                    table.ForeignKey(
                        name: "fk_mitigation_dependencies_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_mitigation_dependencies_mitigation_id",
                        column: x => x.mitigation_id,
                        principalTable: "mitigations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mitigation_dependencies_prerequisite_id",
                        column: x => x.prerequisite_id,
                        principalTable: "mitigations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "mitigation_economics",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    mitigation_id = table.Column<int>(type: "int(11)", nullable: false),
                    treatment_option = table.Column<int>(type: "int(11)", nullable: false),
                    transfer_counterparty = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    cost_one_time = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    cost_annual = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    cost_side_effects_annual = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    cost_horizon_years = table.Column<int>(type: "int(11)", nullable: true),
                    cost_basis = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    effort_person_days = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    duration_days = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    updated_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_mitigation_economics_cost_complete", "(`cost_one_time` IS NULL) = (`cost_annual` IS NULL) AND (`cost_annual` IS NULL) = (`cost_side_effects_annual` IS NULL)");
                    table.CheckConstraint("ck_mitigation_economics_cost_non_negative", "(`cost_one_time` IS NULL OR `cost_one_time` >= 0) AND (`cost_annual` IS NULL OR `cost_annual` >= 0) AND (`cost_side_effects_annual` IS NULL OR `cost_side_effects_annual` >= 0)");
                    table.CheckConstraint("ck_mitigation_economics_horizon", "`cost_horizon_years` IS NULL OR (`cost_horizon_years` >= 1 AND `cost_horizon_years` <= 30)");
                    table.CheckConstraint("ck_mitigation_economics_treatment_option", "`treatment_option` >= 1 AND `treatment_option` <= 4");
                    table.ForeignKey(
                        name: "fk_mitigation_economics_mitigation_id",
                        column: x => x.mitigation_id,
                        principalTable: "mitigations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mitigation_economics_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_targets",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    target_score = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: true),
                    target_expected_loss = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    target_date = table.Column<DateOnly>(type: "date", nullable: true),
                    rationale = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    set_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_targets_expected_loss", "`target_expected_loss` IS NULL OR `target_expected_loss` >= 0");
                    table.CheckConstraint("ck_risk_targets_level", "`target_score` IS NOT NULL OR `target_expected_loss` IS NOT NULL");
                    table.CheckConstraint("ck_risk_targets_score", "`target_score` IS NULL OR (`target_score` >= 0 AND `target_score` <= 10)");
                    table.ForeignKey(
                        name: "fk_risk_targets_risk_id",
                        column: x => x.risk_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_risk_targets_set_by_id",
                        column: x => x.set_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_mitigation_tasks_completion_evidence_by_id",
                table: "mitigation_tasks",
                column: "completion_evidence_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_mitigation_dependencies_created_by_id",
                table: "mitigation_dependencies",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_mitigation_dependencies_prerequisite_id",
                table: "mitigation_dependencies",
                column: "prerequisite_id");

            migrationBuilder.CreateIndex(
                name: "uq_mitigation_dependencies_mitigation_id_prerequisite_id",
                table: "mitigation_dependencies",
                columns: new[] { "mitigation_id", "prerequisite_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_mitigation_economics_updated_by_id",
                table: "mitigation_economics",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_mitigation_economics_mitigation_id",
                table: "mitigation_economics",
                column: "mitigation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_risk_targets_set_by_id",
                table: "risk_targets",
                column: "set_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_risk_targets_risk_id",
                table: "risk_targets",
                column: "risk_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_mitigation_tasks_completion_evidence_by_id",
                table: "mitigation_tasks",
                column: "completion_evidence_by_id",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mitigation_tasks_completion_evidence_by_id",
                table: "mitigation_tasks");

            migrationBuilder.DropTable(
                name: "mitigation_dependencies");

            migrationBuilder.DropTable(
                name: "mitigation_economics");

            migrationBuilder.DropTable(
                name: "risk_targets");

            migrationBuilder.DropIndex(
                name: "idx_mitigation_tasks_completion_evidence_by_id",
                table: "mitigation_tasks");

            migrationBuilder.DropColumn(
                name: "quant_residual_ale_mean",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "acceptance_criterion",
                table: "mitigation_tasks");

            migrationBuilder.DropColumn(
                name: "completion_evidence",
                table: "mitigation_tasks");

            migrationBuilder.DropColumn(
                name: "completion_evidence_at",
                table: "mitigation_tasks");

            migrationBuilder.DropColumn(
                name: "completion_evidence_by_id",
                table: "mitigation_tasks");
        }
    }
}
