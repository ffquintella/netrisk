using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class RiskIRPConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_risks_IncidentResponsePlans_IncidentResponsePlanId",
                table: "risks");

            migrationBuilder.AlterColumn<int>(
                name: "IncidentResponsePlanId",
                table: "risks",
                type: "int(11)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_risk_irp",
                table: "risks",
                column: "IncidentResponsePlanId",
                principalTable: "IncidentResponsePlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_risk_irp",
                table: "risks");

            migrationBuilder.AlterColumn<int>(
                name: "IncidentResponsePlanId",
                table: "risks",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_risks_IncidentResponsePlans_IncidentResponsePlanId",
                table: "risks",
                column: "IncidentResponsePlanId",
                principalTable: "IncidentResponsePlans",
                principalColumn: "Id");
        }
    }
}
