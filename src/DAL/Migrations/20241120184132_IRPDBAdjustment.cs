using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class IRPDBAdjustment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_irp_last_exercised_by",
                table: "IncidentResponsePlans");

            migrationBuilder.DropForeignKey(
                name: "fk_irp_last_reviewed_by",
                table: "IncidentResponsePlans");

            migrationBuilder.DropForeignKey(
                name: "fk_irp_last_tested_by",
                table: "IncidentResponsePlans");

            migrationBuilder.AlterColumn<int>(
                name: "LastTestedById",
                table: "IncidentResponsePlans",
                type: "int(11)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int(11)");

            migrationBuilder.AlterColumn<int>(
                name: "LastReviewedById",
                table: "IncidentResponsePlans",
                type: "int(11)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int(11)");

            migrationBuilder.AlterColumn<int>(
                name: "LastExercisedById",
                table: "IncidentResponsePlans",
                type: "int(11)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int(11)");

            migrationBuilder.AlterColumn<bool>(
                name: "HasBeenUpdated",
                table: "IncidentResponsePlans",
                type: "tinyint(1)",
                nullable: false,
                defaultValueSql: "0",
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");

            migrationBuilder.AlterColumn<bool>(
                name: "HasBeenReviewed",
                table: "IncidentResponsePlans",
                type: "tinyint(1)",
                nullable: false,
                defaultValueSql: "0",
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");

            migrationBuilder.AlterColumn<bool>(
                name: "HasBeenExercised",
                table: "IncidentResponsePlans",
                type: "tinyint(1)",
                nullable: false,
                defaultValueSql: "0",
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");

            migrationBuilder.AlterColumn<bool>(
                name: "HasBeenApproved",
                table: "IncidentResponsePlans",
                type: "tinyint(1)",
                nullable: false,
                defaultValueSql: "0",
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");

            migrationBuilder.AddForeignKey(
                name: "fk_irp_last_exercised_by",
                table: "IncidentResponsePlans",
                column: "LastExercisedById",
                principalTable: "entities",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "fk_irp_last_reviewed_by",
                table: "IncidentResponsePlans",
                column: "LastReviewedById",
                principalTable: "entities",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "fk_irp_last_tested_by",
                table: "IncidentResponsePlans",
                column: "LastTestedById",
                principalTable: "entities",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_irp_last_exercised_by",
                table: "IncidentResponsePlans");

            migrationBuilder.DropForeignKey(
                name: "fk_irp_last_reviewed_by",
                table: "IncidentResponsePlans");

            migrationBuilder.DropForeignKey(
                name: "fk_irp_last_tested_by",
                table: "IncidentResponsePlans");

            migrationBuilder.AlterColumn<int>(
                name: "LastTestedById",
                table: "IncidentResponsePlans",
                type: "int(11)",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "LastReviewedById",
                table: "IncidentResponsePlans",
                type: "int(11)",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "LastExercisedById",
                table: "IncidentResponsePlans",
                type: "int(11)",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "HasBeenUpdated",
                table: "IncidentResponsePlans",
                type: "tinyint(1)",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldDefaultValueSql: "0");

            migrationBuilder.AlterColumn<bool>(
                name: "HasBeenReviewed",
                table: "IncidentResponsePlans",
                type: "tinyint(1)",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldDefaultValueSql: "0");

            migrationBuilder.AlterColumn<bool>(
                name: "HasBeenExercised",
                table: "IncidentResponsePlans",
                type: "tinyint(1)",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldDefaultValueSql: "0");

            migrationBuilder.AlterColumn<bool>(
                name: "HasBeenApproved",
                table: "IncidentResponsePlans",
                type: "tinyint(1)",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldDefaultValueSql: "0");

            migrationBuilder.AddForeignKey(
                name: "fk_irp_last_exercised_by",
                table: "IncidentResponsePlans",
                column: "LastExercisedById",
                principalTable: "entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_irp_last_reviewed_by",
                table: "IncidentResponsePlans",
                column: "LastReviewedById",
                principalTable: "entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_irp_last_tested_by",
                table: "IncidentResponsePlans",
                column: "LastTestedById",
                principalTable: "entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
