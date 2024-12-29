using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class NewIncidentFields2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Incidents",
                type: "varchar(255)",
                nullable: false,
                defaultValue: "",
                collation: "utf8mb3_general_ci")
                .Annotation("MySql:CharSet", "utf8mb3");

            migrationBuilder.AddColumn<int>(
                name: "ImpactedEntityId",
                table: "Incidents",
                type: "int(11)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_category",
                table: "Incidents",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_ImpactedEntityId",
                table: "Incidents",
                column: "ImpactedEntityId");

            migrationBuilder.AddForeignKey(
                name: "fk_inc_impacted_entity",
                table: "Incidents",
                column: "ImpactedEntityId",
                principalTable: "entities",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_inc_impacted_entity",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "idx_category",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_ImpactedEntityId",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "ImpactedEntityId",
                table: "Incidents");
        }
    }
}
