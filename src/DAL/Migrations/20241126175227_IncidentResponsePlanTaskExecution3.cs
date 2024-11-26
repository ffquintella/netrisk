using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class IncidentResponsePlanTaskExecution3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_irpt_executions_created_by",
                table: "IncidentResponsePlanTaskExecution");

            migrationBuilder.RenameTable(
                name: "IncidentResponsePlanTaskExecution",
                newName: "IncidentResponsePlanTaskExecutions");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTaskExecution_TaskId",
                table: "IncidentResponsePlanTaskExecutions",
                newName: "IX_IncidentResponsePlanTaskExecutions_TaskId");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTaskExecution_PlanExecutionId",
                table: "IncidentResponsePlanTaskExecutions",
                newName: "IX_IncidentResponsePlanTaskExecutions_PlanExecutionId");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTaskExecution_ExecutedById",
                table: "IncidentResponsePlanTaskExecutions",
                newName: "IX_IncidentResponsePlanTaskExecutions_ExecutedById");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTaskExecution_CreatedById",
                table: "IncidentResponsePlanTaskExecutions",
                newName: "IX_IncidentResponsePlanTaskExecutions_CreatedById");

            migrationBuilder.AlterColumn<int>(
                name: "CreatedById",
                table: "IncidentResponsePlanTaskExecutions",
                type: "int(11)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int(11)");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "IncidentResponsePlanTaskExecutions",
                type: "datetime",
                nullable: false,
                defaultValueSql: "current_timestamp()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUpdatedAt",
                table: "IncidentResponsePlanTaskExecutions",
                type: "datetime",
                nullable: true,
                defaultValueSql: "current_timestamp()");

            migrationBuilder.AddColumn<int>(
                name: "LastUpdatedById",
                table: "IncidentResponsePlanTaskExecutions",
                type: "int(11)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlanTaskExecutions_LastUpdatedById",
                table: "IncidentResponsePlanTaskExecutions",
                column: "LastUpdatedById");

            migrationBuilder.AddForeignKey(
                name: "fk_irpt_executions_created_by",
                table: "IncidentResponsePlanTaskExecutions",
                column: "CreatedById",
                principalTable: "user",
                principalColumn: "value");

            migrationBuilder.AddForeignKey(
                name: "fk_irpt_executions_last_updated_by",
                table: "IncidentResponsePlanTaskExecutions",
                column: "LastUpdatedById",
                principalTable: "user",
                principalColumn: "value");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_irpt_executions_created_by",
                table: "IncidentResponsePlanTaskExecutions");

            migrationBuilder.DropForeignKey(
                name: "fk_irpt_executions_last_updated_by",
                table: "IncidentResponsePlanTaskExecutions");

            migrationBuilder.DropIndex(
                name: "IX_IncidentResponsePlanTaskExecutions_LastUpdatedById",
                table: "IncidentResponsePlanTaskExecutions");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "IncidentResponsePlanTaskExecutions");

            migrationBuilder.DropColumn(
                name: "LastUpdatedAt",
                table: "IncidentResponsePlanTaskExecutions");

            migrationBuilder.DropColumn(
                name: "LastUpdatedById",
                table: "IncidentResponsePlanTaskExecutions");

            migrationBuilder.RenameTable(
                name: "IncidentResponsePlanTaskExecutions",
                newName: "IncidentResponsePlanTaskExecution");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTaskExecutions_TaskId",
                table: "IncidentResponsePlanTaskExecution",
                newName: "IX_IncidentResponsePlanTaskExecution_TaskId");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTaskExecutions_PlanExecutionId",
                table: "IncidentResponsePlanTaskExecution",
                newName: "IX_IncidentResponsePlanTaskExecution_PlanExecutionId");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTaskExecutions_ExecutedById",
                table: "IncidentResponsePlanTaskExecution",
                newName: "IX_IncidentResponsePlanTaskExecution_ExecutedById");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTaskExecutions_CreatedById",
                table: "IncidentResponsePlanTaskExecution",
                newName: "IX_IncidentResponsePlanTaskExecution_CreatedById");

            migrationBuilder.AlterColumn<int>(
                name: "CreatedById",
                table: "IncidentResponsePlanTaskExecution",
                type: "int(11)",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_irpt_executions_created_by",
                table: "IncidentResponsePlanTaskExecution",
                column: "CreatedById",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
