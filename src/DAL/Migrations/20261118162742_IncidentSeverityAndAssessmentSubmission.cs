using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class IncidentSeverityAndAssessmentSubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "resolved_at",
                table: "incidents",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "severity",
                table: "incidents",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "score",
                table: "assessment_runs",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "submitted_at",
                table: "assessment_runs",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "assessment_answer_id",
                table: "assessment_run_answers",
                type: "int(11)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_assessment_run_answers_assessment_answer_id",
                table: "assessment_run_answers",
                column: "assessment_answer_id");

            migrationBuilder.AddForeignKey(
                name: "fk_assessment_run_answers_assessment_answer_id",
                table: "assessment_run_answers",
                column: "assessment_answer_id",
                principalTable: "assessment_answers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_assessment_run_answers_assessment_answer_id",
                table: "assessment_run_answers");

            migrationBuilder.DropIndex(
                name: "idx_assessment_run_answers_assessment_answer_id",
                table: "assessment_run_answers");

            migrationBuilder.DropColumn(
                name: "resolved_at",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "severity",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "score",
                table: "assessment_runs");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                table: "assessment_runs");

            migrationBuilder.DropColumn(
                name: "assessment_answer_id",
                table: "assessment_run_answers");
        }
    }
}
