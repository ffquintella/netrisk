using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class HostAssessmentAndEntityAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "repository",
                table: "vulnerabilities",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "entity_restamp_policy",
                table: "trendmicro_connections",
                type: "int(11)",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "repository",
                table: "scan_imports",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "entity_restamp_policy",
                table: "issue_tracker_connections",
                type: "int(11)",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "entity_source",
                table: "hosts",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "last_assessed_at",
                table: "hosts",
                type: "datetime",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "repository",
                table: "vulnerabilities");

            migrationBuilder.DropColumn(
                name: "entity_restamp_policy",
                table: "trendmicro_connections");

            migrationBuilder.DropColumn(
                name: "repository",
                table: "scan_imports");

            migrationBuilder.DropColumn(
                name: "entity_restamp_policy",
                table: "issue_tracker_connections");

            migrationBuilder.DropColumn(
                name: "entity_source",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "last_assessed_at",
                table: "hosts");
        }
    }
}
