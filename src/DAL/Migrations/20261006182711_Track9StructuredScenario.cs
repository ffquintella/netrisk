using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track9StructuredScenario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "evidence_confidence",
                table: "risks",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scenario_cause",
                table: "risks",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "scenario_central_event",
                table: "risks",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "scenario_consequences",
                table: "risks",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "scenario_vulnerability",
                table: "risks",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<int>(
                name: "assessment_id",
                table: "pending_risks",
                type: "int(11)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int(11)");

            migrationBuilder.AlterColumn<int>(
                name: "assessment_answer_id",
                table: "pending_risks",
                type: "int(11)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int(11)");

            migrationBuilder.AddColumn<int>(
                name: "entity_id",
                table: "pending_risks",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "origin",
                table: "pending_risks",
                type: "int(11)",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "submitted_by_id",
                table: "pending_risks",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "kind",
                table: "incidents",
                type: "int(11)",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "idx_pending_risks_entity_id",
                table: "pending_risks",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_pending_risks_submitted_by_id",
                table: "pending_risks",
                column: "submitted_by_id");

            migrationBuilder.AddForeignKey(
                name: "fk_pending_risks_entity_id",
                table: "pending_risks",
                column: "entity_id",
                principalTable: "entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_pending_risks_submitted_by_id",
                table: "pending_risks",
                column: "submitted_by_id",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pending_risks_entity_id",
                table: "pending_risks");

            migrationBuilder.DropForeignKey(
                name: "fk_pending_risks_submitted_by_id",
                table: "pending_risks");

            migrationBuilder.DropIndex(
                name: "idx_pending_risks_entity_id",
                table: "pending_risks");

            migrationBuilder.DropIndex(
                name: "idx_pending_risks_submitted_by_id",
                table: "pending_risks");

            migrationBuilder.DropColumn(
                name: "evidence_confidence",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "scenario_cause",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "scenario_central_event",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "scenario_consequences",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "scenario_vulnerability",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "entity_id",
                table: "pending_risks");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "pending_risks");

            migrationBuilder.DropColumn(
                name: "submitted_by_id",
                table: "pending_risks");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "incidents");

            migrationBuilder.AlterColumn<int>(
                name: "assessment_id",
                table: "pending_risks",
                type: "int(11)",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "assessment_answer_id",
                table: "pending_risks",
                type: "int(11)",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldNullable: true);
        }
    }
}
