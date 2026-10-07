using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track9TailRisk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "risk_appetite_tail_limits",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    appetite_id = table.Column<int>(type: "int(11)", nullable: false),
                    max_scenario_expected_loss = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    max_scenario_p95 = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    max_scenario_cvar95 = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    max_portfolio_expected_loss = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    max_portfolio_p95 = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    max_portfolio_cvar95 = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    rationale = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    updated_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_appetite_tail_limits_any", "`max_scenario_expected_loss` IS NOT NULL OR `max_scenario_p95` IS NOT NULL OR `max_scenario_cvar95` IS NOT NULL OR `max_portfolio_expected_loss` IS NOT NULL OR `max_portfolio_p95` IS NOT NULL OR `max_portfolio_cvar95` IS NOT NULL");
                    table.CheckConstraint("ck_risk_appetite_tail_limits_non_negative", "(`max_scenario_expected_loss` IS NULL OR `max_scenario_expected_loss` >= 0) AND (`max_scenario_p95` IS NULL OR `max_scenario_p95` >= 0) AND (`max_scenario_cvar95` IS NULL OR `max_scenario_cvar95` >= 0) AND (`max_portfolio_expected_loss` IS NULL OR `max_portfolio_expected_loss` >= 0) AND (`max_portfolio_p95` IS NULL OR `max_portfolio_p95` >= 0) AND (`max_portfolio_cvar95` IS NULL OR `max_portfolio_cvar95` >= 0)");
                    table.ForeignKey(
                        name: "fk_risk_appetite_tail_limits_appetite_id",
                        column: x => x.appetite_id,
                        principalTable: "risk_appetites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_risk_appetite_tail_limits_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_correlations",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_a_id = table.Column<int>(type: "int(11)", nullable: false),
                    risk_b_id = table.Column<int>(type: "int(11)", nullable: false),
                    coefficient = table.Column<decimal>(type: "decimal(4,3)", precision: 4, scale: 3, nullable: false),
                    rationale = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    updated_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_correlations_coefficient", "`coefficient` >= 0 AND `coefficient` <= 1");
                    table.CheckConstraint("ck_risk_correlations_order", "`risk_a_id` < `risk_b_id`");
                    table.ForeignKey(
                        name: "fk_risk_correlations_risk_a_id",
                        column: x => x.risk_a_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_risk_correlations_risk_b_id",
                        column: x => x.risk_b_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_risk_correlations_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_loss_components",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    component = table.Column<int>(type: "int(11)", nullable: false),
                    loss_min = table.Column<double>(type: "double", nullable: false),
                    loss_most_likely = table.Column<double>(type: "double", nullable: false),
                    loss_max = table.Column<double>(type: "double", nullable: false),
                    basis = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    updated_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_loss_components_component", "`component` >= 1 AND `component` <= 7");
                    table.CheckConstraint("ck_risk_loss_components_fine_basis", "`component` <> 6 OR `basis` IS NOT NULL");
                    table.CheckConstraint("ck_risk_loss_components_range", "`loss_min` >= 0 AND `loss_min` <= `loss_most_likely` AND `loss_most_likely` <= `loss_max`");
                    table.ForeignKey(
                        name: "fk_risk_loss_components_risk_id",
                        column: x => x.risk_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_risk_loss_components_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_tail_statistics",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    run = table.Column<int>(type: "int(11)", nullable: false),
                    iterations = table.Column<int>(type: "int(11)", nullable: false),
                    seed = table.Column<int>(type: "int(11)", nullable: false),
                    confidence_level = table.Column<decimal>(type: "decimal(4,3)", precision: 4, scale: 3, nullable: false),
                    lef_min = table.Column<double>(type: "double", nullable: false),
                    lef_most_likely = table.Column<double>(type: "double", nullable: false),
                    lef_max = table.Column<double>(type: "double", nullable: false),
                    magnitude_min = table.Column<double>(type: "double", nullable: false),
                    magnitude_most_likely = table.Column<double>(type: "double", nullable: false),
                    magnitude_max = table.Column<double>(type: "double", nullable: false),
                    magnitude_source = table.Column<int>(type: "int(11)", nullable: false),
                    mitigation_effectiveness = table.Column<double>(type: "double", nullable: false),
                    expected_loss = table.Column<double>(type: "double", nullable: false),
                    expected_loss_ci_low = table.Column<double>(type: "double", nullable: false),
                    expected_loss_ci_high = table.Column<double>(type: "double", nullable: false),
                    p95 = table.Column<double>(type: "double", nullable: false),
                    p95_ci_low = table.Column<double>(type: "double", nullable: false),
                    p95_ci_high = table.Column<double>(type: "double", nullable: false),
                    cvar95 = table.Column<double>(type: "double", nullable: false),
                    cvar95_ci_low = table.Column<double>(type: "double", nullable: false),
                    cvar95_ci_high = table.Column<double>(type: "double", nullable: false),
                    probability_of_loss = table.Column<double>(type: "double", nullable: false),
                    conditional_loss = table.Column<double>(type: "double", nullable: true),
                    computed_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_tail_statistics_iterations", "`iterations` >= 1000 AND `iterations` <= 100000");
                    table.CheckConstraint("ck_risk_tail_statistics_magnitude_source", "`magnitude_source` >= 1 AND `magnitude_source` <= 2");
                    table.CheckConstraint("ck_risk_tail_statistics_mitigation_effectiveness", "`mitigation_effectiveness` >= 0 AND `mitigation_effectiveness` <= 1");
                    table.CheckConstraint("ck_risk_tail_statistics_probability_of_loss", "`probability_of_loss` >= 0 AND `probability_of_loss` <= 1");
                    table.CheckConstraint("ck_risk_tail_statistics_run", "`run` >= 1 AND `run` <= 2");
                    table.ForeignKey(
                        name: "fk_risk_tail_statistics_risk_id",
                        column: x => x.risk_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_tail_components",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    tail_statistics_id = table.Column<int>(type: "int(11)", nullable: false),
                    component = table.Column<int>(type: "int(11)", nullable: false),
                    loss_min = table.Column<double>(type: "double", nullable: false),
                    loss_most_likely = table.Column<double>(type: "double", nullable: false),
                    loss_max = table.Column<double>(type: "double", nullable: false),
                    expected_loss = table.Column<double>(type: "double", nullable: false),
                    cvar95 = table.Column<double>(type: "double", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_tail_components_component", "`component` >= 1 AND `component` <= 7");
                    table.ForeignKey(
                        name: "fk_risk_tail_components_tail_statistics_id",
                        column: x => x.tail_statistics_id,
                        principalTable: "risk_tail_statistics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_risk_appetite_tail_limits_updated_by_id",
                table: "risk_appetite_tail_limits",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_risk_appetite_tail_limits_appetite_id",
                table: "risk_appetite_tail_limits",
                column: "appetite_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_risk_correlations_risk_b_id",
                table: "risk_correlations",
                column: "risk_b_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_correlations_updated_by_id",
                table: "risk_correlations",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_risk_correlations_risk_a_id_risk_b_id",
                table: "risk_correlations",
                columns: new[] { "risk_a_id", "risk_b_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_risk_loss_components_updated_by_id",
                table: "risk_loss_components",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_risk_loss_components_risk_id_component",
                table: "risk_loss_components",
                columns: new[] { "risk_id", "component" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_risk_tail_components_tail_statistics_id_component",
                table: "risk_tail_components",
                columns: new[] { "tail_statistics_id", "component" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_risk_tail_statistics_risk_id_run",
                table: "risk_tail_statistics",
                columns: new[] { "risk_id", "run" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "risk_appetite_tail_limits");

            migrationBuilder.DropTable(
                name: "risk_correlations");

            migrationBuilder.DropTable(
                name: "risk_loss_components");

            migrationBuilder.DropTable(
                name: "risk_tail_components");

            migrationBuilder.DropTable(
                name: "risk_tail_statistics");
        }
    }
}
