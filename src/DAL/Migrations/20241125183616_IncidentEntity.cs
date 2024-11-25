using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class IncidentEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IncidentId",
                table: "nr_files",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IncidentId",
                table: "nr_actions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Incidents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(250)", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    Description = table.Column<string>(type: "text", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    CreationDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()"),
                    CreatedById = table.Column<int>(type: "int(11)", nullable: false),
                    LastUpdate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()"),
                    UpdatedById = table.Column<int>(type: "int(11)", nullable: true),
                    Status = table.Column<int>(type: "int(6)", nullable: false),
                    Report = table.Column<string>(type: "text", nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    Notes = table.Column<string>(type: "text", nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    Impact = table.Column<string>(type: "text", nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    Cause = table.Column<string>(type: "text", nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    Resolution = table.Column<string>(type: "text", nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    Duration = table.Column<long>(type: "bigint", nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "current_timestamp()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "fk_inc_created_by",
                        column: x => x.CreatedById,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inc_updated_by",
                        column: x => x.UpdatedById,
                        principalTable: "user",
                        principalColumn: "value");
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "IncidentToIncidentResponsePlan",
                columns: table => new
                {
                    IncidentId = table.Column<int>(type: "int", nullable: false),
                    IncidentResponsePlanId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentToIncidentResponsePlan", x => new { x.IncidentId, x.IncidentResponsePlanId });
                    table.ForeignKey(
                        name: "FK_IncidentToIncidentResponsePlan_IncidentResponsePlans_Inciden~",
                        column: x => x.IncidentResponsePlanId,
                        principalTable: "IncidentResponsePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IncidentToIncidentResponsePlan_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_nr_files_IncidentId",
                table: "nr_files",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_nr_actions_IncidentId",
                table: "nr_actions",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "idx_inc_name",
                table: "Incidents",
                column: "Name")
                .Annotation("MySql:FullTextIndex", true);

            migrationBuilder.CreateIndex(
                name: "idx_inc_repo",
                table: "Incidents",
                column: "Name")
                .Annotation("MySql:FullTextIndex", true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_CreatedById",
                table: "Incidents",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_UpdatedById",
                table: "Incidents",
                column: "UpdatedById");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentToIncidentResponsePlan_IncidentResponsePlanId",
                table: "IncidentToIncidentResponsePlan",
                column: "IncidentResponsePlanId");

            migrationBuilder.AddForeignKey(
                name: "fk_inc_actions",
                table: "nr_actions",
                column: "IncidentId",
                principalTable: "Incidents",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "fk_inc_attachments",
                table: "nr_files",
                column: "IncidentId",
                principalTable: "Incidents",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_inc_actions",
                table: "nr_actions");

            migrationBuilder.DropForeignKey(
                name: "fk_inc_attachments",
                table: "nr_files");

            migrationBuilder.DropTable(
                name: "IncidentToIncidentResponsePlan");

            migrationBuilder.DropTable(
                name: "Incidents");

            migrationBuilder.DropIndex(
                name: "IX_nr_files_IncidentId",
                table: "nr_files");

            migrationBuilder.DropIndex(
                name: "IX_nr_actions_IncidentId",
                table: "nr_actions");

            migrationBuilder.DropColumn(
                name: "IncidentId",
                table: "nr_files");

            migrationBuilder.DropColumn(
                name: "IncidentId",
                table: "nr_actions");
        }
    }
}
