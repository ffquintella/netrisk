using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class biometricTransactionsDetailing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DateTime",
                table: "BiometricTransaction",
                newName: "StartTime");

            migrationBuilder.AlterColumn<string>(
                name: "BiometricLivenessAnchor",
                table: "BiometricTransaction",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: false,
                collation: "utf8mb4_unicode_ci",
                oldClrType: typeof(string),
                oldType: "varchar(255)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.AddColumn<DateTime>(
                name: "ResultTime",
                table: "BiometricTransaction",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "TransactionId",
                table: "BiometricTransaction",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "TransactionResultDetails",
                table: "BiometricTransaction",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ValidationObjectData",
                table: "BiometricTransaction",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ValidationSequence",
                table: "BiometricTransaction",
                type: "longtext",
                nullable: false,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResultTime",
                table: "BiometricTransaction");

            migrationBuilder.DropColumn(
                name: "TransactionId",
                table: "BiometricTransaction");

            migrationBuilder.DropColumn(
                name: "TransactionResultDetails",
                table: "BiometricTransaction");

            migrationBuilder.DropColumn(
                name: "ValidationObjectData",
                table: "BiometricTransaction");

            migrationBuilder.DropColumn(
                name: "ValidationSequence",
                table: "BiometricTransaction");

            migrationBuilder.RenameColumn(
                name: "StartTime",
                table: "BiometricTransaction",
                newName: "DateTime");

            migrationBuilder.AlterColumn<string>(
                name: "BiometricLivenessAnchor",
                table: "BiometricTransaction",
                type: "varchar(255)",
                nullable: false,
                collation: "utf8mb4_unicode_ci",
                oldClrType: typeof(string),
                oldType: "varchar(1000)",
                oldMaxLength: 1000)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "utf8mb4_unicode_ci");
        }
    }
}
