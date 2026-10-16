using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class RiskIndexBridgesAndPreviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "risk_index_bridges",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint(20)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    from_profile_id = table.Column<int>(type: "int(11)", nullable: false),
                    to_profile_id = table.Column<int>(type: "int(11)", nullable: false),
                    bridge_date = table.Column<DateOnly>(type: "date", nullable: false),
                    scope_kind = table.Column<int>(type: "int(11)", nullable: false),
                    scope_ref = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    category = table.Column<int>(type: "int(11)", nullable: false),
                    value_old = table.Column<double>(type: "double", nullable: false),
                    value_new = table.Column<double>(type: "double", nullable: false),
                    overlay = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    overlay_partial = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_index_bridges_category", "`category` >= 0 AND `category` <= 6");
                    table.CheckConstraint("ck_risk_index_bridges_profiles", "`from_profile_id` <> `to_profile_id`");
                    table.CheckConstraint("ck_risk_index_bridges_scope_kind", "`scope_kind` >= 1 AND `scope_kind` <= 4");
                    table.ForeignKey(
                        name: "fk_risk_index_bridges_from_profile_id",
                        column: x => x.from_profile_id,
                        principalTable: "risk_index_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_risk_index_bridges_to_profile_id",
                        column: x => x.to_profile_id,
                        principalTable: "risk_index_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_index_preview_requests",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    profile_id = table.Column<int>(type: "int(11)", nullable: false),
                    parameters_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<int>(type: "int(11)", nullable: false),
                    progress = table.Column<int>(type: "int(11)", nullable: false, defaultValue: 0),
                    requested_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    heartbeat_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    finished_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    report = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_risk_index_preview_requests_progress", "`progress` >= 0 AND `progress` <= 100");
                    table.CheckConstraint("ck_risk_index_preview_requests_status", "`status` >= 1 AND `status` <= 4");
                    table.ForeignKey(
                        name: "fk_risk_index_preview_requests_profile_id",
                        column: x => x.profile_id,
                        principalTable: "risk_index_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_risk_index_preview_requests_requested_by_id",
                        column: x => x.requested_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_bridges_entity_id",
                table: "risk_index_bridges",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_bridges_from_profile_id",
                table: "risk_index_bridges",
                column: "from_profile_id");

            migrationBuilder.CreateIndex(
                name: "uq_risk_index_bridges_to_scope_category",
                table: "risk_index_bridges",
                columns: new[] { "to_profile_id", "scope_kind", "scope_ref", "category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_preview_requests_profile_id",
                table: "risk_index_preview_requests",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_preview_requests_requested_by_id",
                table: "risk_index_preview_requests",
                column: "requested_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_preview_requests_status",
                table: "risk_index_preview_requests",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "risk_index_bridges");

            migrationBuilder.DropTable(
                name: "risk_index_preview_requests");
        }
    }
}
