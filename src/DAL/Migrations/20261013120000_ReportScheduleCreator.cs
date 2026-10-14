using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations;

/// <summary>
/// T237 review: who created a report schedule, so the scheduled job reads under that user's entity scope as it stands at
/// run time instead of the job host's global one. Nullable — a schedule created before this column has no recorded
/// creator and does not run until an administrator saves it — and set null with the user, so a deleted creator's schedule
/// stops rather than running as nobody. Numbered SQL: Structure/105.sql, Data/105.sql.
/// </summary>
public partial class ReportScheduleCreator : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "created_by_id",
            table: "report_schedules",
            type: "int(11)",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "idx_report_schedules_created_by_id",
            table: "report_schedules",
            column: "created_by_id");

        migrationBuilder.AddForeignKey(
            name: "fk_report_schedules_created_by_id",
            table: "report_schedules",
            column: "created_by_id",
            principalTable: "user",
            principalColumn: "value",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_report_schedules_created_by_id",
            table: "report_schedules");

        migrationBuilder.DropIndex(
            name: "idx_report_schedules_created_by_id",
            table: "report_schedules");

        migrationBuilder.DropColumn(
            name: "created_by_id",
            table: "report_schedules");
    }
}
