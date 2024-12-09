using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class IRPApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovedById",
                table: "IncidentResponsePlans",
                type: "int(11)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlans_ApprovedById",
                table: "IncidentResponsePlans",
                column: "ApprovedById");

            migrationBuilder.AddForeignKey(
                name: "fk_irp_approved_by",
                table: "IncidentResponsePlans",
                column: "ApprovedById",
                principalTable: "entities",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_irp_approved_by",
                table: "IncidentResponsePlans");

            migrationBuilder.DropIndex(
                name: "IX_IncidentResponsePlans_ApprovedById",
                table: "IncidentResponsePlans");

            migrationBuilder.DropColumn(
                name: "ApprovedById",
                table: "IncidentResponsePlans");
        }
    }
}
