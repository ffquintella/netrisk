using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <summary>
    /// Track 2 milestone 2.4.3 — persisted task-dependency edges for incident response plans and
    /// the blocked-task override record.
    ///
    /// Hand-authored rather than tool-generated: the installed dotnet-ef (10.0.11) regenerates the
    /// model snapshot in a form this EF Core + Pomelo combination cannot build a relational model
    /// from, which trips SchemaConsistencyTests on any migration — including an empty one — so the
    /// snapshot is edited in the committed style instead. The runtime upgrade path is
    /// DB/Structure/76.sql, verified end to end against MariaDB by Phase7IrpDependenciesTests.
    /// </summary>
    public partial class AddIrpTaskDependenciesAndOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "overridden_at",
                table: "incident_response_plan_tasks",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "overridden_by_id",
                table: "incident_response_plan_tasks",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "override_reason",
                table: "incident_response_plan_tasks",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "incident_response_plan_task_dependencies",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    task_id = table.Column<int>(type: "int(11)", nullable: false),
                    depends_on_task_id = table.Column<int>(type: "int(11)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_irptd_depends_on_task_id",
                        column: x => x.depends_on_task_id,
                        principalTable: "incident_response_plan_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_irptd_task_id",
                        column: x => x.task_id,
                        principalTable: "incident_response_plan_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_irpt_overridden_by_id",
                table: "incident_response_plan_tasks",
                column: "overridden_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_irptd_depends_on_task_id",
                table: "incident_response_plan_task_dependencies",
                column: "depends_on_task_id");

            migrationBuilder.CreateIndex(
                name: "uq_irptd_task_depends_on",
                table: "incident_response_plan_task_dependencies",
                columns: new[] { "task_id", "depends_on_task_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_irpt_overridden_by_id",
                table: "incident_response_plan_tasks",
                column: "overridden_by_id",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_irpt_overridden_by_id",
                table: "incident_response_plan_tasks");

            migrationBuilder.DropTable(
                name: "incident_response_plan_task_dependencies");

            migrationBuilder.DropIndex(
                name: "idx_irpt_overridden_by_id",
                table: "incident_response_plan_tasks");

            migrationBuilder.DropColumn(
                name: "overridden_at",
                table: "incident_response_plan_tasks");

            migrationBuilder.DropColumn(
                name: "overridden_by_id",
                table: "incident_response_plan_tasks");

            migrationBuilder.DropColumn(
                name: "override_reason",
                table: "incident_response_plan_tasks");
        }
    }
}
