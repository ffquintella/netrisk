using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class IncidentResponsePlanTaskExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_irp_task_executions",
                table: "IncidentResponsePlanExecutions");

            migrationBuilder.DropIndex(
                name: "IX_IncidentResponsePlanExecutions_TaskId",
                table: "IncidentResponsePlanExecutions");

            migrationBuilder.DropColumn(
                name: "TaskId",
                table: "IncidentResponsePlanExecutions");

            migrationBuilder.AddColumn<int>(
                name: "IncidentResponsePlanTaskExecutionId",
                table: "nr_files",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "IncidentResponsePlans",
                type: "int(11)",
                nullable: false,
                defaultValueSql: "0",
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "IncidentResponsePlanExecutions",
                type: "int(11)",
                nullable: false,
                defaultValueSql: "0",
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "CreatedById",
                table: "IncidentResponsePlanExecutions",
                type: "int(11)",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "IncidentResponsePlanTaskExecution",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PlanExecutionId = table.Column<int>(type: "int(11)", nullable: false),
                    TaskId = table.Column<int>(type: "int(11)", nullable: false),
                    ExecutionDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()"),
                    Duration = table.Column<long>(type: "bigint", nullable: false),
                    ExecutedById = table.Column<int>(type: "int(11)", nullable: true),
                    CreatedById = table.Column<int>(type: "int(11)", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "0"),
                    IsTest = table.Column<bool>(type: "tinyint(1)", nullable: true, defaultValueSql: "0"),
                    IsExercise = table.Column<bool>(type: "tinyint(1)", nullable: true, defaultValueSql: "0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "fk_irpt_executions_created_by",
                        column: x => x.CreatedById,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_irpt_executions_entity",
                        column: x => x.ExecutedById,
                        principalTable: "entities",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "fk_irpt_executions_plan",
                        column: x => x.PlanExecutionId,
                        principalTable: "IncidentResponsePlanExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_irpt_executions_task",
                        column: x => x.TaskId,
                        principalTable: "IncidentResponsePlanTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_nr_files_IncidentResponsePlanTaskExecutionId",
                table: "nr_files",
                column: "IncidentResponsePlanTaskExecutionId");

            migrationBuilder.CreateIndex(
                name: "idx_irpt_status2",
                table: "IncidentResponsePlanTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_irpt_status",
                table: "IncidentResponsePlans",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_irpt_status1",
                table: "IncidentResponsePlanExecutions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlanExecutions_CreatedById",
                table: "IncidentResponsePlanExecutions",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "idx_irpt_exec_status",
                table: "IncidentResponsePlanTaskExecution",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlanTaskExecution_CreatedById",
                table: "IncidentResponsePlanTaskExecution",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlanTaskExecution_ExecutedById",
                table: "IncidentResponsePlanTaskExecution",
                column: "ExecutedById");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlanTaskExecution_PlanExecutionId",
                table: "IncidentResponsePlanTaskExecution",
                column: "PlanExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlanTaskExecution_TaskId",
                table: "IncidentResponsePlanTaskExecution",
                column: "TaskId");

            migrationBuilder.AddForeignKey(
                name: "fk_irp_executions_created_by",
                table: "IncidentResponsePlanExecutions",
                column: "CreatedById",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_irpt_executions_attachments",
                table: "nr_files",
                column: "IncidentResponsePlanTaskExecutionId",
                principalTable: "IncidentResponsePlanTaskExecution",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_irp_executions_created_by",
                table: "IncidentResponsePlanExecutions");

            migrationBuilder.DropForeignKey(
                name: "fk_irpt_executions_attachments",
                table: "nr_files");

            migrationBuilder.DropTable(
                name: "IncidentResponsePlanTaskExecution");

            migrationBuilder.DropIndex(
                name: "IX_nr_files_IncidentResponsePlanTaskExecutionId",
                table: "nr_files");

            migrationBuilder.DropIndex(
                name: "idx_irpt_status2",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.DropIndex(
                name: "idx_irpt_status",
                table: "IncidentResponsePlans");

            migrationBuilder.DropIndex(
                name: "idx_irpt_status1",
                table: "IncidentResponsePlanExecutions");

            migrationBuilder.DropIndex(
                name: "IX_IncidentResponsePlanExecutions_CreatedById",
                table: "IncidentResponsePlanExecutions");

            migrationBuilder.DropColumn(
                name: "IncidentResponsePlanTaskExecutionId",
                table: "nr_files");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "IncidentResponsePlanExecutions");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "IncidentResponsePlans",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldDefaultValueSql: "0");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "IncidentResponsePlanExecutions",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldDefaultValueSql: "0");

            migrationBuilder.AddColumn<int>(
                name: "TaskId",
                table: "IncidentResponsePlanExecutions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlanExecutions_TaskId",
                table: "IncidentResponsePlanExecutions",
                column: "TaskId");

            migrationBuilder.AddForeignKey(
                name: "fk_irp_task_executions",
                table: "IncidentResponsePlanExecutions",
                column: "TaskId",
                principalTable: "IncidentResponsePlanTasks",
                principalColumn: "Id");
        }
    }
}
