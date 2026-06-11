using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track6Phase1SafeFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "idx_irpt_sequencial",
                table: "IncidentResponsePlanTasks",
                newName: "idx_irpt_sequential");

            migrationBuilder.RenameIndex(
                name: "idx_irpt_optinal",
                table: "IncidentResponsePlanTasks",
                newName: "idx_irpt_optional");

            migrationBuilder.RenameIndex(
                name: "idx_biometic_id",
                table: "BiometricTransaction",
                newName: "idx_biometric_transaction_id");

            migrationBuilder.RenameIndex(
                name: "idx_biometic_anchor",
                table: "BiometricTransaction",
                newName: "idx_biometric_transaction_anchor");

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_update",
                table: "mitigations",
                type: "timestamp",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "timestamp",
                oldDefaultValueSql: "'0000-00-00 00:00:00'");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "next_review",
                table: "mgmt_reviews",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldDefaultValueSql: "'0000-00-00'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "idx_irpt_sequential",
                table: "IncidentResponsePlanTasks",
                newName: "idx_irpt_sequencial");

            migrationBuilder.RenameIndex(
                name: "idx_irpt_optional",
                table: "IncidentResponsePlanTasks",
                newName: "idx_irpt_optinal");

            migrationBuilder.RenameIndex(
                name: "idx_biometric_transaction_id",
                table: "BiometricTransaction",
                newName: "idx_biometic_id");

            migrationBuilder.RenameIndex(
                name: "idx_biometric_transaction_anchor",
                table: "BiometricTransaction",
                newName: "idx_biometic_anchor");

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_update",
                table: "mitigations",
                type: "timestamp",
                nullable: false,
                defaultValueSql: "'0000-00-00 00:00:00'",
                oldClrType: typeof(DateTime),
                oldType: "timestamp",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "next_review",
                table: "mgmt_reviews",
                type: "date",
                nullable: false,
                defaultValueSql: "'0000-00-00'",
                oldClrType: typeof(DateOnly),
                oldType: "date");
        }
    }
}
