using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track9DecisionCycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incident_backtests",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    incident_id = table.Column<int>(type: "int(11)", nullable: false),
                    note = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    assessed_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    assessed_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_incident_backtests_assessed_by_id",
                        column: x => x.assessed_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_incident_backtests_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_archives",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    closure_id = table.Column<int>(type: "int(11)", nullable: true),
                    status = table.Column<int>(type: "int(11)", nullable: false),
                    justification = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    previous_status = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    archived_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    archived_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    next_review_due_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    last_reviewed_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    review_notified_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    reopened_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    reopen_origin = table.Column<int>(type: "int(11)", nullable: true),
                    reopened_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    reopen_reason = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reopen_event_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_archives_reopen_origin", "`reopen_origin` IS NULL OR (`reopen_origin` >= 1 AND `reopen_origin` <= 3)");
                    table.CheckConstraint("ck_risk_archives_reopened", "(`status` = 1 AND `reopened_at` IS NULL AND `reopen_origin` IS NULL) OR (`status` = 2 AND `reopened_at` IS NOT NULL AND `reopen_origin` IS NOT NULL AND `reopen_reason` IS NOT NULL)");
                    table.CheckConstraint("ck_risk_archives_status", "`status` >= 1 AND `status` <= 2");
                    table.ForeignKey(
                        name: "fk_risk_archives_archived_by_id",
                        column: x => x.archived_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_archives_closure_id",
                        column: x => x.closure_id,
                        principalTable: "closures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_archives_reopen_event_id",
                        column: x => x.reopen_event_id,
                        principalTable: "reassessment_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_archives_reopened_by_id",
                        column: x => x.reopened_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_archives_risk_id",
                        column: x => x.risk_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_committees",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mandate = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    required_approvals = table.Column<int>(type: "int(11)", nullable: false),
                    retired_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    updated_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_committees_required_approvals", "`required_approvals` >= 2 AND `required_approvals` <= 50");
                    table.ForeignKey(
                        name: "fk_risk_committees_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_committees_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "incident_backtest_risks",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    backtest_id = table.Column<int>(type: "int(11)", nullable: false),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_incident_backtest_risks_backtest_id",
                        column: x => x.backtest_id,
                        principalTable: "incident_backtests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_incident_backtest_risks_risk_id",
                        column: x => x.risk_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_archive_conditions",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    archive_id = table.Column<int>(type: "int(11)", nullable: false),
                    trigger_type = table.Column<int>(type: "int(11)", nullable: false),
                    description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_archive_conditions_trigger_type", "`trigger_type` >= 1 AND `trigger_type` <= 6");
                    table.ForeignKey(
                        name: "fk_risk_archive_conditions_archive_id",
                        column: x => x.archive_id,
                        principalTable: "risk_archives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_archive_reviews",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    archive_id = table.Column<int>(type: "int(11)", nullable: false),
                    outcome = table.Column<int>(type: "int(11)", nullable: false),
                    note = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reviewed_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    reviewed_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    next_review_due_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_archive_reviews_next_due", "(`outcome` = 1 AND `next_review_due_at` IS NOT NULL) OR (`outcome` = 2 AND `next_review_due_at` IS NULL)");
                    table.CheckConstraint("ck_risk_archive_reviews_outcome", "`outcome` >= 1 AND `outcome` <= 2");
                    table.ForeignKey(
                        name: "fk_risk_archive_reviews_archive_id",
                        column: x => x.archive_id,
                        principalTable: "risk_archives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_risk_archive_reviews_reviewed_by_id",
                        column: x => x.reviewed_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_committee_decisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    committee_id = table.Column<int>(type: "int(11)", nullable: false),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    kind = table.Column<int>(type: "int(11)", nullable: false),
                    renews_acceptance_id = table.Column<int>(type: "int(11)", nullable: true),
                    status = table.Column<int>(type: "int(11)", nullable: false),
                    required_approvals = table.Column<int>(type: "int(11)", nullable: false),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    business_justification = table.Column<string>(type: "text", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    compensating_controls = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expires_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    minutes_reference = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    opened_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    opened_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    closed_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    withdrawal_reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    acceptance_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    version = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_committee_decisions_closed", "(`status` = 1 AND `closed_at` IS NULL) OR (`status` <> 1 AND `closed_at` IS NOT NULL)");
                    table.CheckConstraint("ck_risk_committee_decisions_kind", "`kind` >= 1 AND `kind` <= 2");
                    table.CheckConstraint("ck_risk_committee_decisions_required_approvals", "`required_approvals` >= 2 AND `required_approvals` <= 50");
                    table.CheckConstraint("ck_risk_committee_decisions_status", "`status` >= 1 AND `status` <= 4");
                    table.CheckConstraint("ck_risk_committee_decisions_withdrawn", "`status` <> 4 OR `withdrawal_reason` IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_risk_committee_decisions_acceptance_id",
                        column: x => x.acceptance_id,
                        principalTable: "risk_acceptances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_committee_decisions_committee_id",
                        column: x => x.committee_id,
                        principalTable: "risk_committees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_risk_committee_decisions_opened_by_id",
                        column: x => x.opened_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_committee_decisions_renews_acceptance_id",
                        column: x => x.renews_acceptance_id,
                        principalTable: "risk_acceptances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_committee_decisions_risk_id",
                        column: x => x.risk_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_committee_members",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    committee_id = table.Column<int>(type: "int(11)", nullable: false),
                    user_id = table.Column<int>(type: "int(11)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_risk_committee_members_committee_id",
                        column: x => x.committee_id,
                        principalTable: "risk_committees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_risk_committee_members_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_committee_members_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_committee_votes",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    decision_id = table.Column<int>(type: "int(11)", nullable: false),
                    voter_id = table.Column<int>(type: "int(11)", nullable: true),
                    choice = table.Column<int>(type: "int(11)", nullable: false),
                    comment = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    cast_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_committee_votes_choice", "`choice` >= 1 AND `choice` <= 3");
                    table.ForeignKey(
                        name: "fk_risk_committee_votes_decision_id",
                        column: x => x.decision_id,
                        principalTable: "risk_committee_decisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_risk_committee_votes_voter_id",
                        column: x => x.voter_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_incident_backtest_risks_risk_id",
                table: "incident_backtest_risks",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "uq_incident_backtest_risks_backtest_id_risk_id",
                table: "incident_backtest_risks",
                columns: new[] { "backtest_id", "risk_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_incident_backtests_assessed_by_id",
                table: "incident_backtests",
                column: "assessed_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_incident_backtests_incident_id",
                table: "incident_backtests",
                column: "incident_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_risk_archive_conditions_archive_id_trigger_type",
                table: "risk_archive_conditions",
                columns: new[] { "archive_id", "trigger_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_risk_archive_reviews_archive_id",
                table: "risk_archive_reviews",
                column: "archive_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_archive_reviews_reviewed_by_id",
                table: "risk_archive_reviews",
                column: "reviewed_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_archives_archived_by_id",
                table: "risk_archives",
                column: "archived_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_archives_closure_id",
                table: "risk_archives",
                column: "closure_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_archives_reopen_event_id",
                table: "risk_archives",
                column: "reopen_event_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_archives_reopened_by_id",
                table: "risk_archives",
                column: "reopened_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_archives_risk_id",
                table: "risk_archives",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_archives_status_next_review_due_at",
                table: "risk_archives",
                columns: new[] { "status", "next_review_due_at" });

            migrationBuilder.CreateIndex(
                name: "idx_risk_committee_decisions_acceptance_id",
                table: "risk_committee_decisions",
                column: "acceptance_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_committee_decisions_committee_id",
                table: "risk_committee_decisions",
                column: "committee_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_committee_decisions_opened_by_id",
                table: "risk_committee_decisions",
                column: "opened_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_committee_decisions_renews_acceptance_id",
                table: "risk_committee_decisions",
                column: "renews_acceptance_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_committee_decisions_risk_id_status",
                table: "risk_committee_decisions",
                columns: new[] { "risk_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_risk_committee_members_created_by_id",
                table: "risk_committee_members",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_committee_members_user_id",
                table: "risk_committee_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_risk_committee_members_committee_id_user_id",
                table: "risk_committee_members",
                columns: new[] { "committee_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_risk_committee_votes_voter_id",
                table: "risk_committee_votes",
                column: "voter_id");

            migrationBuilder.CreateIndex(
                name: "uq_risk_committee_votes_decision_id_voter_id",
                table: "risk_committee_votes",
                columns: new[] { "decision_id", "voter_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_risk_committees_entity_id",
                table: "risk_committees",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_committees_updated_by_id",
                table: "risk_committees",
                column: "updated_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incident_backtest_risks");

            migrationBuilder.DropTable(
                name: "risk_archive_conditions");

            migrationBuilder.DropTable(
                name: "risk_archive_reviews");

            migrationBuilder.DropTable(
                name: "risk_committee_members");

            migrationBuilder.DropTable(
                name: "risk_committee_votes");

            migrationBuilder.DropTable(
                name: "incident_backtests");

            migrationBuilder.DropTable(
                name: "risk_archives");

            migrationBuilder.DropTable(
                name: "risk_committee_decisions");

            migrationBuilder.DropTable(
                name: "risk_committees");
        }
    }
}
