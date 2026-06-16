using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddUserEntityRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "entity_id",
                table: "risks",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "entity_id",
                table: "incidents",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "entity_id",
                table: "hosts",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "entity_id",
                table: "assessments",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "current_page_index",
                table: "assessment_runs",
                type: "int(11)",
                nullable: false,
                defaultValueSql: "'1'");

            migrationBuilder.AddColumn<int>(
                name: "progress_percentage",
                table: "assessment_runs",
                type: "int(11)",
                nullable: false,
                defaultValueSql: "'0'");

            migrationBuilder.AddColumn<string>(
                name: "condition_json",
                table: "assessment_questions",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "explanation_markdown",
                table: "assessment_questions",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "page_number",
                table: "assessment_questions",
                type: "int(11)",
                nullable: false,
                defaultValueSql: "'1'");

            migrationBuilder.AddColumn<int>(
                name: "parent_question_id",
                table: "assessment_questions",
                type: "int(11)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "assessment_run_answers",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    assessment_run_id = table.Column<int>(type: "int(11)", nullable: false),
                    assessment_question_id = table.Column<int>(type: "int(11)", nullable: false),
                    answer_content_json = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    last_updated_at = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_assessment_run_answers_question",
                        column: x => x.assessment_question_id,
                        principalTable: "assessment_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_assessment_run_answers_run",
                        column: x => x.assessment_run_id,
                        principalTable: "assessment_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "irp_templates",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    matching_rules_json = table.Column<string>(type: "text", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_enabled = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "report_templates",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    owner_id = table.Column<int>(type: "int(11)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()"),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_report_templates_owner",
                        column: x => x.owner_id,
                        principalTable: "user",
                        principalColumn: "value");
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "user_entity_roles",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<int>(type: "int(11)", nullable: false),
                    entity_id = table.Column<int>(type: "int(11)", nullable: false),
                    role_id = table.Column<int>(type: "int(11)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()"),
                    revoked_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_entity_roles_entity",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_entity_roles_role",
                        column: x => x.role_id,
                        principalTable: "role",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_entity_roles_user",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "irp_template_tasks",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    irp_template_id = table.Column<int>(type: "int(11)", nullable: false),
                    title = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    instructions_markdown = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    assignee_rule_json = table.Column<string>(type: "text", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    due_offset_seconds = table.Column<int>(type: "int(11)", nullable: false),
                    predecessor_task_id = table.Column<int>(type: "int(11)", nullable: true),
                    requires_confirmation = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_irp_template_tasks_predecessor",
                        column: x => x.predecessor_task_id,
                        principalTable: "irp_template_tasks",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_irp_template_tasks_template",
                        column: x => x.irp_template_id,
                        principalTable: "irp_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "report_template_versions",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    template_id = table.Column<int>(type: "int(11)", nullable: false),
                    version = table.Column<int>(type: "int(11)", nullable: false),
                    layout_json = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    branding_json = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_report_template_versions_template",
                        column: x => x.template_id,
                        principalTable: "report_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "report_schedules",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    report_template_version_id = table.Column<int>(type: "int(11)", nullable: false),
                    frequency_cron = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    timezone = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, defaultValueSql: "'UTC'", collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recipients_json = table.Column<string>(type: "text", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    last_run_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    last_status = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_report_schedules_template_version",
                        column: x => x.report_template_version_id,
                        principalTable: "report_template_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "IX_risks_entity_id",
                table: "risks",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_entity_id",
                table: "incidents",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_hosts_entity_id",
                table: "hosts",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_assessments_entity_id",
                table: "assessments",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "fk_assessment_questions_parent",
                table: "assessment_questions",
                column: "parent_question_id");

            migrationBuilder.CreateIndex(
                name: "fk_assessment_run_answers_question",
                table: "assessment_run_answers",
                column: "assessment_question_id");

            migrationBuilder.CreateIndex(
                name: "fk_assessment_run_answers_run",
                table: "assessment_run_answers",
                column: "assessment_run_id");

            migrationBuilder.CreateIndex(
                name: "fk_irp_template_tasks_predecessor",
                table: "irp_template_tasks",
                column: "predecessor_task_id");

            migrationBuilder.CreateIndex(
                name: "fk_irp_template_tasks_template",
                table: "irp_template_tasks",
                column: "irp_template_id");

            migrationBuilder.CreateIndex(
                name: "fk_report_schedules_template_version",
                table: "report_schedules",
                column: "report_template_version_id");

            migrationBuilder.CreateIndex(
                name: "fk_report_template_versions_template",
                table: "report_template_versions",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "fk_report_templates_owner",
                table: "report_templates",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "fk_user_entity_roles_entity",
                table: "user_entity_roles",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "fk_user_entity_roles_role",
                table: "user_entity_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "fk_user_entity_roles_user",
                table: "user_entity_roles",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_assessment_questions_parent",
                table: "assessment_questions",
                column: "parent_question_id",
                principalTable: "assessment_questions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_assessments_entity",
                table: "assessments",
                column: "entity_id",
                principalTable: "entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_hosts_entity",
                table: "hosts",
                column: "entity_id",
                principalTable: "entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_incidents_entity",
                table: "incidents",
                column: "entity_id",
                principalTable: "entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_risks_entity",
                table: "risks",
                column: "entity_id",
                principalTable: "entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_assessment_questions_parent",
                table: "assessment_questions");

            migrationBuilder.DropForeignKey(
                name: "fk_assessments_entity",
                table: "assessments");

            migrationBuilder.DropForeignKey(
                name: "fk_hosts_entity",
                table: "hosts");

            migrationBuilder.DropForeignKey(
                name: "fk_incidents_entity",
                table: "incidents");

            migrationBuilder.DropForeignKey(
                name: "fk_risks_entity",
                table: "risks");

            migrationBuilder.DropTable(
                name: "assessment_run_answers");

            migrationBuilder.DropTable(
                name: "irp_template_tasks");

            migrationBuilder.DropTable(
                name: "report_schedules");

            migrationBuilder.DropTable(
                name: "user_entity_roles");

            migrationBuilder.DropTable(
                name: "irp_templates");

            migrationBuilder.DropTable(
                name: "report_template_versions");

            migrationBuilder.DropTable(
                name: "report_templates");

            migrationBuilder.DropIndex(
                name: "IX_risks_entity_id",
                table: "risks");

            migrationBuilder.DropIndex(
                name: "IX_incidents_entity_id",
                table: "incidents");

            migrationBuilder.DropIndex(
                name: "IX_hosts_entity_id",
                table: "hosts");

            migrationBuilder.DropIndex(
                name: "IX_assessments_entity_id",
                table: "assessments");

            migrationBuilder.DropIndex(
                name: "fk_assessment_questions_parent",
                table: "assessment_questions");

            migrationBuilder.DropColumn(
                name: "entity_id",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "entity_id",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "entity_id",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "entity_id",
                table: "assessments");

            migrationBuilder.DropColumn(
                name: "current_page_index",
                table: "assessment_runs");

            migrationBuilder.DropColumn(
                name: "progress_percentage",
                table: "assessment_runs");

            migrationBuilder.DropColumn(
                name: "condition_json",
                table: "assessment_questions");

            migrationBuilder.DropColumn(
                name: "explanation_markdown",
                table: "assessment_questions");

            migrationBuilder.DropColumn(
                name: "page_number",
                table: "assessment_questions");

            migrationBuilder.DropColumn(
                name: "parent_question_id",
                table: "assessment_questions");
        }
    }
}
