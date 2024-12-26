using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class NewIncidentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignedToId",
                table: "Incidents",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Recomendations",
                table: "Incidents",
                type: "text",
                nullable: true,
                collation: "utf8mb3_general_ci")
                .Annotation("MySql:CharSet", "utf8mb3");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReportDate",
                table: "Incidents",
                type: "datetime",
                nullable: false,
                defaultValueSql: "current_timestamp()");

            migrationBuilder.AddColumn<int>(
                name: "ReportEntityId",
                table: "Incidents",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportedBy",
                table: "Incidents",
                type: "varchar(255)",
                nullable: true,
                collation: "utf8mb3_general_ci")
                .Annotation("MySql:CharSet", "utf8mb3");

            migrationBuilder.AddColumn<bool>(
                name: "ReportedByEntity",
                table: "Incidents",
                type: "tinyint(1)",
                nullable: false,
                defaultValueSql: "0");

            migrationBuilder.CreateIndex(
                name: "idx_reported_by",
                table: "Incidents",
                column: "ReportedBy")
                .Annotation("MySql:FullTextIndex", true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_AssignedToId",
                table: "Incidents",
                column: "AssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_ReportEntityId",
                table: "Incidents",
                column: "ReportEntityId");

            migrationBuilder.AddForeignKey(
                name: "fk_inc_report_entity",
                table: "Incidents",
                column: "ReportEntityId",
                principalTable: "entities",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "fk_inc_report_user",
                table: "Incidents",
                column: "AssignedToId",
                principalTable: "user",
                principalColumn: "value");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_inc_report_entity",
                table: "Incidents");

            migrationBuilder.DropForeignKey(
                name: "fk_inc_report_user",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "idx_reported_by",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_AssignedToId",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_ReportEntityId",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "AssignedToId",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "Recomendations",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "ReportDate",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "ReportEntityId",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "ReportedBy",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "ReportedByEntity",
                table: "Incidents");
        }
    }
}
