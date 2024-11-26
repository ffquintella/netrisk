using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class IncidentResponsePlanTaskExecution2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "IncidentResponsePlanExecutions",
                type: "text",
                nullable: true,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.AlterColumn<bool>(
                name: "IsTest",
                table: "IncidentResponsePlanExecutions",
                type: "tinyint(1)",
                nullable: true,
                defaultValueSql: "0",
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");

            migrationBuilder.AlterColumn<bool>(
                name: "IsExercise",
                table: "IncidentResponsePlanExecutions",
                type: "tinyint(1)",
                nullable: true,
                defaultValueSql: "0",
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");

            migrationBuilder.AlterColumn<string>(
                name: "ExecutionTrigger",
                table: "IncidentResponsePlanExecutions",
                type: "text",
                nullable: false,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.AlterColumn<string>(
                name: "ExecutionResult",
                table: "IncidentResponsePlanExecutions",
                type: "text",
                nullable: false,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ExecutionDate",
                table: "IncidentResponsePlanExecutions",
                type: "datetime",
                nullable: true,
                defaultValueSql: "current_timestamp()",
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)");

            migrationBuilder.AlterColumn<long>(
                name: "Duration",
                table: "IncidentResponsePlanExecutions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(TimeSpan),
                oldType: "time(6)");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreationDate",
                table: "IncidentResponsePlanExecutions",
                type: "datetime",
                nullable: true,
                defaultValueSql: "current_timestamp()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUpdateDate",
                table: "IncidentResponsePlanExecutions",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastUpdatedById",
                table: "IncidentResponsePlanExecutions",
                type: "int(11)",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlanExecutions_LastUpdatedById",
                table: "IncidentResponsePlanExecutions",
                column: "LastUpdatedById");

            migrationBuilder.AddForeignKey(
                name: "fk_irp_executions_updated_by",
                table: "IncidentResponsePlanExecutions",
                column: "LastUpdatedById",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_irp_executions_updated_by",
                table: "IncidentResponsePlanExecutions");

            migrationBuilder.DropIndex(
                name: "IX_IncidentResponsePlanExecutions_LastUpdatedById",
                table: "IncidentResponsePlanExecutions");

            migrationBuilder.DropColumn(
                name: "CreationDate",
                table: "IncidentResponsePlanExecutions");

            migrationBuilder.DropColumn(
                name: "LastUpdateDate",
                table: "IncidentResponsePlanExecutions");

            migrationBuilder.DropColumn(
                name: "LastUpdatedById",
                table: "IncidentResponsePlanExecutions");

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "IncidentResponsePlanExecutions",
                type: "longtext",
                nullable: true,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.AlterColumn<bool>(
                name: "IsTest",
                table: "IncidentResponsePlanExecutions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldNullable: true,
                oldDefaultValueSql: "0");

            migrationBuilder.AlterColumn<bool>(
                name: "IsExercise",
                table: "IncidentResponsePlanExecutions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldNullable: true,
                oldDefaultValueSql: "0");

            migrationBuilder.AlterColumn<string>(
                name: "ExecutionTrigger",
                table: "IncidentResponsePlanExecutions",
                type: "longtext",
                nullable: false,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "text")
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.AlterColumn<string>(
                name: "ExecutionResult",
                table: "IncidentResponsePlanExecutions",
                type: "longtext",
                nullable: false,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "text")
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ExecutionDate",
                table: "IncidentResponsePlanExecutions",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime",
                oldNullable: true,
                oldDefaultValueSql: "current_timestamp()");

            migrationBuilder.AlterColumn<TimeSpan>(
                name: "Duration",
                table: "IncidentResponsePlanExecutions",
                type: "time(6)",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");
        }
    }
}
