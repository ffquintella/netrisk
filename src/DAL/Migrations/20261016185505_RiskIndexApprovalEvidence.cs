using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class RiskIndexApprovalEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "submitted_by_id",
                table: "risk_index_profiles",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "risk_index_profile_id",
                table: "nr_files",
                type: "int(11)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "permission_grant_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint(20)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    kind = table.Column<int>(type: "int(11)", nullable: false),
                    action = table.Column<int>(type: "int(11)", nullable: false),
                    role_id = table.Column<int>(type: "int(11)", nullable: true),
                    role_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_id = table.Column<int>(type: "int(11)", nullable: true),
                    permission_key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    old_value = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    new_value = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    actor_id = table.Column<int>(type: "int(11)", nullable: true),
                    actor = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_permission_grant_events_action", "`action` >= 1 AND `action` <= 2");
                    table.CheckConstraint("ck_permission_grant_events_kind", "`kind` >= 1 AND `kind` <= 5");
                    table.ForeignKey(
                        name: "fk_permission_grant_events_role_id",
                        column: x => x.role_id,
                        principalTable: "role",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_permission_grant_events_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_index_profile_editors",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    profile_id = table.Column<int>(type: "int(11)", nullable: false),
                    user_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_risk_index_profile_editors_profile_id",
                        column: x => x.profile_id,
                        principalTable: "risk_index_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_risk_index_profile_editors_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_profiles_submitted_by_id",
                table: "risk_index_profiles",
                column: "submitted_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_nr_files_risk_index_profile_id",
                table: "nr_files",
                column: "risk_index_profile_id");

            migrationBuilder.CreateIndex(
                name: "idx_permission_grant_events_actor_id",
                table: "permission_grant_events",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "idx_permission_grant_events_kind_role_name",
                table: "permission_grant_events",
                columns: new[] { "kind", "role_name" });

            migrationBuilder.CreateIndex(
                name: "idx_permission_grant_events_role_id_permission_key",
                table: "permission_grant_events",
                columns: new[] { "role_id", "permission_key" });

            migrationBuilder.CreateIndex(
                name: "idx_permission_grant_events_user_id_kind",
                table: "permission_grant_events",
                columns: new[] { "user_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "idx_risk_index_profile_editors_user_id",
                table: "risk_index_profile_editors",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_risk_index_profile_editors_profile_id_user_id",
                table: "risk_index_profile_editors",
                columns: new[] { "profile_id", "user_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_nr_files_risk_index_profile_id",
                table: "nr_files",
                column: "risk_index_profile_id",
                principalTable: "risk_index_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_risk_index_profiles_submitted_by_id",
                table: "risk_index_profiles",
                column: "submitted_by_id",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_nr_files_risk_index_profile_id",
                table: "nr_files");

            migrationBuilder.DropForeignKey(
                name: "fk_risk_index_profiles_submitted_by_id",
                table: "risk_index_profiles");

            migrationBuilder.DropTable(
                name: "permission_grant_events");

            migrationBuilder.DropTable(
                name: "risk_index_profile_editors");

            migrationBuilder.DropIndex(
                name: "idx_risk_index_profiles_submitted_by_id",
                table: "risk_index_profiles");

            migrationBuilder.DropIndex(
                name: "idx_nr_files_risk_index_profile_id",
                table: "nr_files");

            migrationBuilder.DropColumn(
                name: "submitted_by_id",
                table: "risk_index_profiles");

            migrationBuilder.DropColumn(
                name: "risk_index_profile_id",
                table: "nr_files");
        }
    }
}
