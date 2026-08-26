using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track8GovernanceSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "business_rank",
                table: "risks",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "review_requested",
                table: "risks",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "review_requested_at",
                table: "risks",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_requested_reason",
                table: "risks",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<float>(
                name: "residual_risk",
                table: "risk_scoring_history",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "quant_ale_mean",
                table: "risk_scoring",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "quant_ale_p10",
                table: "risk_scoring",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "quant_ale_p50",
                table: "risk_scoring",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "quant_ale_p90",
                table: "risk_scoring",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "quant_computed_at",
                table: "risk_scoring",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "quant_lef_max",
                table: "risk_scoring",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "quant_lef_min",
                table: "risk_scoring",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "quant_lef_most_likely",
                table: "risk_scoring",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "quant_loss_exceedance_curve",
                table: "risk_scoring",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "quant_loss_max",
                table: "risk_scoring",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "quant_loss_min",
                table: "risk_scoring",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "quant_loss_most_likely",
                table: "risk_scoring",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "quant_residual_ale_p10",
                table: "risk_scoring",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "quant_residual_ale_p50",
                table: "risk_scoring",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "quant_residual_ale_p90",
                table: "risk_scoring",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "quant_seed",
                table: "risk_scoring",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "residual_risk",
                table: "risk_scoring",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "residual_updated_at",
                table: "risk_scoring",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "renewed_from_id",
                table: "risk_acceptances",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "requested_by_id",
                table: "risk_acceptances",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "risk_id",
                table: "risk_acceptances",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "start_date",
                table: "risk_acceptances",
                type: "datetime",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "dismissal_reason",
                table: "pending_risks",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "promoted_risk_id",
                table: "pending_risks",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "status",
                table: "pending_risks",
                type: "int(11)",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "triaged_at",
                table: "pending_risks",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "triaged_by_id",
                table: "pending_risks",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_countersignature",
                table: "mgmt_reviews",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "second_review_at",
                table: "mgmt_reviews",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "second_reviewer_id",
                table: "mgmt_reviews",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "segregation_override_reason",
                table: "mgmt_reviews",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "definition",
                table: "likelihood",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "probability_max",
                table: "likelihood",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "probability_min",
                table: "likelihood",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "definition",
                table: "impact",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "impact_max",
                table: "impact",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "impact_min",
                table: "impact",
                type: "double",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    entity_type = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    entity_id = table.Column<int>(type: "int(11)", nullable: false),
                    field = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    old_value = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    new_value = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    action = table.Column<int>(type: "int(11)", nullable: false),
                    user_id = table.Column<int>(type: "int(11)", nullable: true),
                    actor = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    occurred_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    correlation_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_logs_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "mitigation_tasks",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    mitigation_id = table.Column<int>(type: "int(11)", nullable: false),
                    title = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    owner_id = table.Column<int>(type: "int(11)", nullable: true),
                    due_date = table.Column<DateTime>(type: "datetime", nullable: true),
                    status = table.Column<int>(type: "int(11)", nullable: false, defaultValue: 1),
                    completed_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    last_notified_days_before = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_mitigation_tasks_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_mitigation_tasks_mitigation_id",
                        column: x => x.mitigation_id,
                        principalTable: "mitigations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mitigation_tasks_owner_id",
                        column: x => x.owner_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_appetites",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    max_acceptable_residual = table.Column<double>(type: "double", nullable: false),
                    dual_approval_threshold = table.Column<double>(type: "double", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_risk_appetites_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_appetites_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_risks_business_rank",
                table: "risks",
                column: "business_rank");

            migrationBuilder.CreateIndex(
                name: "idx_risks_review_requested",
                table: "risks",
                column: "review_requested");

            migrationBuilder.CreateIndex(
                name: "idx_risk_scoring_residual_risk",
                table: "risk_scoring",
                column: "residual_risk");

            migrationBuilder.CreateIndex(
                name: "idx_ra_renewed_from_id",
                table: "risk_acceptances",
                column: "renewed_from_id");

            migrationBuilder.CreateIndex(
                name: "idx_ra_risk_id",
                table: "risk_acceptances",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "IX_risk_acceptances_requested_by_id",
                table: "risk_acceptances",
                column: "requested_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_pending_risks_status",
                table: "pending_risks",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "idx_mgmt_reviews_second_reviewer_id",
                table: "mgmt_reviews",
                column: "second_reviewer_id");

            migrationBuilder.CreateIndex(
                name: "idx_audit_logs_correlation_id",
                table: "audit_logs",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "idx_audit_logs_entity_type_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "idx_audit_logs_occurred_at",
                table: "audit_logs",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_user_id",
                table: "audit_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "idx_mitigation_tasks_mitigation_id",
                table: "mitigation_tasks",
                column: "mitigation_id");

            migrationBuilder.CreateIndex(
                name: "idx_mitigation_tasks_owner_id",
                table: "mitigation_tasks",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "idx_mitigation_tasks_status_due_date",
                table: "mitigation_tasks",
                columns: new[] { "status", "due_date" });

            migrationBuilder.CreateIndex(
                name: "IX_mitigation_tasks_created_by_id",
                table: "mitigation_tasks",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_risk_appetites_created_by_id",
                table: "risk_appetites",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_risk_appetites_entity_id",
                table: "risk_appetites",
                column: "entity_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_mgmt_reviews_second_reviewer_id",
                table: "mgmt_reviews",
                column: "second_reviewer_id",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ra_renewed_from_id",
                table: "risk_acceptances",
                column: "renewed_from_id",
                principalTable: "risk_acceptances",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ra_requested_by_id",
                table: "risk_acceptances",
                column: "requested_by_id",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_ra_risk_id",
                table: "risk_acceptances",
                column: "risk_id",
                principalTable: "risks",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mgmt_reviews_second_reviewer_id",
                table: "mgmt_reviews");

            migrationBuilder.DropForeignKey(
                name: "fk_ra_renewed_from_id",
                table: "risk_acceptances");

            migrationBuilder.DropForeignKey(
                name: "fk_ra_requested_by_id",
                table: "risk_acceptances");

            migrationBuilder.DropForeignKey(
                name: "fk_ra_risk_id",
                table: "risk_acceptances");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "mitigation_tasks");

            migrationBuilder.DropTable(
                name: "risk_appetites");

            migrationBuilder.DropIndex(
                name: "idx_risks_business_rank",
                table: "risks");

            migrationBuilder.DropIndex(
                name: "idx_risks_review_requested",
                table: "risks");

            migrationBuilder.DropIndex(
                name: "idx_risk_scoring_residual_risk",
                table: "risk_scoring");

            migrationBuilder.DropIndex(
                name: "idx_ra_renewed_from_id",
                table: "risk_acceptances");

            migrationBuilder.DropIndex(
                name: "idx_ra_risk_id",
                table: "risk_acceptances");

            migrationBuilder.DropIndex(
                name: "IX_risk_acceptances_requested_by_id",
                table: "risk_acceptances");

            migrationBuilder.DropIndex(
                name: "idx_pending_risks_status",
                table: "pending_risks");

            migrationBuilder.DropIndex(
                name: "idx_mgmt_reviews_second_reviewer_id",
                table: "mgmt_reviews");

            migrationBuilder.DropColumn(
                name: "business_rank",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "review_requested",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "review_requested_at",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "review_requested_reason",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "residual_risk",
                table: "risk_scoring_history");

            migrationBuilder.DropColumn(
                name: "quant_ale_mean",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "quant_ale_p10",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "quant_ale_p50",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "quant_ale_p90",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "quant_computed_at",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "quant_lef_max",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "quant_lef_min",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "quant_lef_most_likely",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "quant_loss_exceedance_curve",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "quant_loss_max",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "quant_loss_min",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "quant_loss_most_likely",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "quant_residual_ale_p10",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "quant_residual_ale_p50",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "quant_residual_ale_p90",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "quant_seed",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "residual_risk",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "residual_updated_at",
                table: "risk_scoring");

            migrationBuilder.DropColumn(
                name: "renewed_from_id",
                table: "risk_acceptances");

            migrationBuilder.DropColumn(
                name: "requested_by_id",
                table: "risk_acceptances");

            migrationBuilder.DropColumn(
                name: "risk_id",
                table: "risk_acceptances");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "risk_acceptances");

            migrationBuilder.DropColumn(
                name: "dismissal_reason",
                table: "pending_risks");

            migrationBuilder.DropColumn(
                name: "promoted_risk_id",
                table: "pending_risks");

            migrationBuilder.DropColumn(
                name: "status",
                table: "pending_risks");

            migrationBuilder.DropColumn(
                name: "triaged_at",
                table: "pending_risks");

            migrationBuilder.DropColumn(
                name: "triaged_by_id",
                table: "pending_risks");

            migrationBuilder.DropColumn(
                name: "requires_countersignature",
                table: "mgmt_reviews");

            migrationBuilder.DropColumn(
                name: "second_review_at",
                table: "mgmt_reviews");

            migrationBuilder.DropColumn(
                name: "second_reviewer_id",
                table: "mgmt_reviews");

            migrationBuilder.DropColumn(
                name: "segregation_override_reason",
                table: "mgmt_reviews");

            migrationBuilder.DropColumn(
                name: "definition",
                table: "likelihood");

            migrationBuilder.DropColumn(
                name: "probability_max",
                table: "likelihood");

            migrationBuilder.DropColumn(
                name: "probability_min",
                table: "likelihood");

            migrationBuilder.DropColumn(
                name: "definition",
                table: "impact");

            migrationBuilder.DropColumn(
                name: "impact_max",
                table: "impact");

            migrationBuilder.DropColumn(
                name: "impact_min",
                table: "impact");
        }
    }
}
