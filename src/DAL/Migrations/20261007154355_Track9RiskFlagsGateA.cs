using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track9RiskFlagsGateA : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "risk_decisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    decision = table.Column<int>(type: "int(11)", nullable: false),
                    source = table.Column<int>(type: "int(11)", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    gate_a_conditions = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    decided_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    decided_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    escalated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_decisions_decision", "`decision` >= 1 AND `decision` <= 4");
                    table.CheckConstraint("ck_risk_decisions_source", "`source` >= 1 AND `source` <= 2");
                    table.ForeignKey(
                        name: "fk_risk_decisions_decided_by_id",
                        column: x => x.decided_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_decisions_risk_id",
                        column: x => x.risk_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_flags",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    flag = table.Column<int>(type: "int(11)", nullable: false),
                    declared = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    declared_reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    declared_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    declared_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    derived = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    derived_basis = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    derived_weight = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: true),
                    derived_changed_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    derived_note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_flags_flag", "`flag` >= 1 AND `flag` <= 12");
                    table.ForeignKey(
                        name: "fk_risk_flags_declared_by_id",
                        column: x => x.declared_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_flags_risk_id",
                        column: x => x.risk_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_risk_decisions_decided_by_id",
                table: "risk_decisions",
                column: "decided_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_decisions_risk_id_decided_at",
                table: "risk_decisions",
                columns: new[] { "risk_id", "decided_at" });

            migrationBuilder.CreateIndex(
                name: "idx_risk_flags_declared_by_id",
                table: "risk_flags",
                column: "declared_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_flags_flag",
                table: "risk_flags",
                column: "flag");

            migrationBuilder.CreateIndex(
                name: "uq_risk_flags_risk_id_flag",
                table: "risk_flags",
                columns: new[] { "risk_id", "flag" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "risk_decisions");

            migrationBuilder.DropTable(
                name: "risk_flags");
        }
    }
}
