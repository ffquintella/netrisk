using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class IRPTName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "idx_irpt_status2",
                table: "IncidentResponsePlanTasks",
                newName: "idx_irpt_status1");

            migrationBuilder.RenameIndex(
                name: "idx_irpt_status",
                table: "IncidentResponsePlans",
                newName: "idx_irp_status");

            migrationBuilder.RenameIndex(
                name: "idx_irpt_status1",
                table: "IncidentResponsePlanExecutions",
                newName: "idx_irpt_status");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "IncidentResponsePlanTasks",
                type: "varchar(255)",
                maxLength: 254,
                nullable: false,
                defaultValue: "",
                collation: "utf8mb3_general_ci")
                .Annotation("MySql:CharSet", "utf8mb3");

            migrationBuilder.CreateIndex(
                name: "idx_irpt_exec_order",
                table: "IncidentResponsePlanTasks",
                column: "ExecutionOrder");

            migrationBuilder.CreateIndex(
                name: "idx_irpt_name",
                table: "IncidentResponsePlanTasks",
                column: "Name")
                .Annotation("MySql:FullTextIndex", true);

            migrationBuilder.CreateIndex(
                name: "idx_irpt_optinal",
                table: "IncidentResponsePlanTasks",
                column: "IsOptional");

            migrationBuilder.CreateIndex(
                name: "idx_irpt_parallel",
                table: "IncidentResponsePlanTasks",
                column: "IsParallel");

            migrationBuilder.CreateIndex(
                name: "idx_irpt_priority",
                table: "IncidentResponsePlanTasks",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "idx_irpt_sequencial",
                table: "IncidentResponsePlanTasks",
                column: "IsSequential");

            migrationBuilder.CreateIndex(
                name: "idx_irp_approved",
                table: "IncidentResponsePlans",
                column: "HasBeenApproved");

            migrationBuilder.CreateIndex(
                name: "idx_irp_lupdate",
                table: "IncidentResponsePlans",
                column: "LastUpdate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_irpt_exec_order",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.DropIndex(
                name: "idx_irpt_name",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.DropIndex(
                name: "idx_irpt_optinal",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.DropIndex(
                name: "idx_irpt_parallel",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.DropIndex(
                name: "idx_irpt_priority",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.DropIndex(
                name: "idx_irpt_sequencial",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.DropIndex(
                name: "idx_irp_approved",
                table: "IncidentResponsePlans");

            migrationBuilder.DropIndex(
                name: "idx_irp_lupdate",
                table: "IncidentResponsePlans");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.RenameIndex(
                name: "idx_irpt_status1",
                table: "IncidentResponsePlanTasks",
                newName: "idx_irpt_status2");

            migrationBuilder.RenameIndex(
                name: "idx_irp_status",
                table: "IncidentResponsePlans",
                newName: "idx_irpt_status");

            migrationBuilder.RenameIndex(
                name: "idx_irpt_status",
                table: "IncidentResponsePlanExecutions",
                newName: "idx_irpt_status1");
        }
    }
}
