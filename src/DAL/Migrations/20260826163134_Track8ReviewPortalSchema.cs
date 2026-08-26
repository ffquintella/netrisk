using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track8ReviewPortalSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_risk_reviewers",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    entity_id = table.Column<int>(type: "int(11)", nullable: false),
                    user_id = table.Column<int>(type: "int(11)", nullable: false),
                    is_primary = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    appointed_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_entity_risk_reviewers_appointed_by_id",
                        column: x => x.appointed_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_entity_risk_reviewers_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_entity_risk_reviewers_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_review_campaigns",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    entity_id = table.Column<int>(type: "int(11)", nullable: false),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    period_start = table.Column<DateTime>(type: "datetime", nullable: false),
                    period_end = table.Column<DateTime>(type: "datetime", nullable: false),
                    due_date = table.Column<DateTime>(type: "datetime", nullable: false),
                    status = table.Column<int>(type: "int(11)", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    last_notified_days_before = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_risk_review_campaigns_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_review_campaign_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    campaign_id = table.Column<int>(type: "int(11)", nullable: false),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    rank = table.Column<int>(type: "int(11)", nullable: true),
                    decision = table.Column<int>(type: "int(11)", nullable: false, defaultValue: 1),
                    decision_notes = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    decided_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    decided_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    risk_acceptance_id = table.Column<int>(type: "int(11)", nullable: true),
                    escalated_to_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_risk_review_campaign_items_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "risk_review_campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_risk_review_campaign_items_decided_by_id",
                        column: x => x.decided_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_review_campaign_items_escalated_to_id",
                        column: x => x.escalated_to_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_review_campaign_items_risk_acceptance_id",
                        column: x => x.risk_acceptance_id,
                        principalTable: "risk_acceptances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_review_campaign_items_risk_id",
                        column: x => x.risk_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_entity_risk_reviewers_user_id",
                table: "entity_risk_reviewers",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_entity_risk_reviewers_appointed_by_id",
                table: "entity_risk_reviewers",
                column: "appointed_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_entity_risk_reviewers_entity_user",
                table: "entity_risk_reviewers",
                columns: new[] { "entity_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_risk_review_campaign_items_risk_id",
                table: "risk_review_campaign_items",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "IX_risk_review_campaign_items_decided_by_id",
                table: "risk_review_campaign_items",
                column: "decided_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_risk_review_campaign_items_escalated_to_id",
                table: "risk_review_campaign_items",
                column: "escalated_to_id");

            migrationBuilder.CreateIndex(
                name: "IX_risk_review_campaign_items_risk_acceptance_id",
                table: "risk_review_campaign_items",
                column: "risk_acceptance_id");

            migrationBuilder.CreateIndex(
                name: "uq_risk_review_campaign_items_campaign_risk",
                table: "risk_review_campaign_items",
                columns: new[] { "campaign_id", "risk_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_risk_review_campaigns_status_due_date",
                table: "risk_review_campaigns",
                columns: new[] { "status", "due_date" });

            migrationBuilder.CreateIndex(
                name: "uq_risk_review_campaigns_entity_period",
                table: "risk_review_campaigns",
                columns: new[] { "entity_id", "period_start", "period_end" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_risk_reviewers");

            migrationBuilder.DropTable(
                name: "risk_review_campaign_items");

            migrationBuilder.DropTable(
                name: "risk_review_campaigns");
        }
    }
}
