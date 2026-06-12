using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track6Phase3Relationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "submitted_by",
                table: "risks",
                type: "int(11)",
                nullable: true,
                defaultValueSql: "'1'",
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldDefaultValueSql: "'1'");

            migrationBuilder.AlterColumn<int>(
                name: "owner",
                table: "risks",
                type: "int(11)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int(11)");

            migrationBuilder.AlterColumn<int>(
                name: "manager",
                table: "risks",
                type: "int(11)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int(11)");

            migrationBuilder.AddColumn<int>(
                name: "reported_by_id",
                table: "incidents",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "tester",
                table: "framework_control_tests",
                type: "int(11)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int(11)");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_reported_by_id",
                table: "incidents",
                column: "reported_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_framework_controls_control_owner",
                table: "framework_controls",
                column: "control_owner");

            migrationBuilder.CreateIndex(
                name: "IX_framework_control_tests_tester",
                table: "framework_control_tests",
                column: "tester");

            migrationBuilder.AddForeignKey(
                name: "fk_framework_control_tests_tester",
                table: "framework_control_tests",
                column: "tester",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_framework_controls_control_owner",
                table: "framework_controls",
                column: "control_owner",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_incidents_reported_by",
                table: "incidents",
                column: "reported_by_id",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_risks_manager",
                table: "risks",
                column: "manager",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_risks_owner",
                table: "risks",
                column: "owner",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_risks_submitted_by",
                table: "risks",
                column: "submitted_by",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_framework_control_tests_tester",
                table: "framework_control_tests");

            migrationBuilder.DropForeignKey(
                name: "fk_framework_controls_control_owner",
                table: "framework_controls");

            migrationBuilder.DropForeignKey(
                name: "fk_incidents_reported_by",
                table: "incidents");

            migrationBuilder.DropForeignKey(
                name: "fk_risks_manager",
                table: "risks");

            migrationBuilder.DropForeignKey(
                name: "fk_risks_owner",
                table: "risks");

            migrationBuilder.DropForeignKey(
                name: "fk_risks_submitted_by",
                table: "risks");

            migrationBuilder.DropIndex(
                name: "IX_incidents_reported_by_id",
                table: "incidents");

            migrationBuilder.DropIndex(
                name: "IX_framework_controls_control_owner",
                table: "framework_controls");

            migrationBuilder.DropIndex(
                name: "IX_framework_control_tests_tester",
                table: "framework_control_tests");

            migrationBuilder.DropColumn(
                name: "reported_by_id",
                table: "incidents");

            migrationBuilder.AlterColumn<int>(
                name: "submitted_by",
                table: "risks",
                type: "int(11)",
                nullable: false,
                defaultValueSql: "'1'",
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldNullable: true,
                oldDefaultValueSql: "'1'");

            migrationBuilder.AlterColumn<int>(
                name: "owner",
                table: "risks",
                type: "int(11)",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "manager",
                table: "risks",
                type: "int(11)",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "tester",
                table: "framework_control_tests",
                type: "int(11)",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldNullable: true);
        }
    }
}
