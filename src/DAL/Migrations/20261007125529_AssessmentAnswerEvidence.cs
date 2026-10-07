using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class AssessmentAnswerEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "assessment_run_answer_id",
                table: "nr_files",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "comment",
                table: "assessment_run_answers",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "idx_nr_files_assessment_run_answer_id",
                table: "nr_files",
                column: "assessment_run_answer_id");

            migrationBuilder.AddForeignKey(
                name: "fk_nr_files_assessment_run_answer_id",
                table: "nr_files",
                column: "assessment_run_answer_id",
                principalTable: "assessment_run_answers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_nr_files_assessment_run_answer_id",
                table: "nr_files");

            migrationBuilder.DropIndex(
                name: "idx_nr_files_assessment_run_answer_id",
                table: "nr_files");

            migrationBuilder.DropColumn(
                name: "assessment_run_answer_id",
                table: "nr_files");

            migrationBuilder.DropColumn(
                name: "comment",
                table: "assessment_run_answers");
        }
    }
}
