using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Track 6 Phase 6b — DESTRUCTIVE removal of the tables deprecated in Phase 6a, after the recorded
    /// observation window. Drops the 23 <c>zz_deprecated_*</c> tables and finally the orphan columns
    /// risks.regulation / risks.project_id. No model change accompanies this phase (the entities and columns
    /// already left the model in 6a); the entity classes are deleted from the project in the same change.
    /// Irreversible: <see cref="Down"/> cannot restore dropped data — recover from the archived dump taken by
    /// the upgrade tool's automatic backup before this phase. Production path is the numbered SQL (73.sql).
    /// </remarks>
    public partial class Track6Phase6bDropDeprecatedTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "zz_deprecated_contributing_risks_impact");

            migrationBuilder.DropTable(
                name: "zz_deprecated_contributing_risks_likelihood");

            migrationBuilder.DropTable(
                name: "zz_deprecated_control_phase");

            migrationBuilder.DropTable(
                name: "zz_deprecated_control_type");

            migrationBuilder.DropTable(
                name: "zz_deprecated_failed_login_attempts");

            migrationBuilder.DropTable(
                name: "zz_deprecated_file_type_extensions");

            migrationBuilder.DropTable(
                name: "zz_deprecated_framework_control_test_audits");

            migrationBuilder.DropTable(
                name: "zz_deprecated_framework_control_test_comments");

            migrationBuilder.DropTable(
                name: "zz_deprecated_framework_control_test_results_to_risks");

            migrationBuilder.DropTable(
                name: "zz_deprecated_framework_control_type_mappings");

            migrationBuilder.DropTable(
                name: "zz_deprecated_mitigation_accept_users");

            migrationBuilder.DropTable(
                name: "zz_deprecated_permission_to_permission_group");

            migrationBuilder.DropTable(
                name: "zz_deprecated_questionnaire_pending_risks");

            migrationBuilder.DropTable(
                name: "zz_deprecated_regulation");

            migrationBuilder.DropTable(
                name: "zz_deprecated_residual_risk_scoring_history");

            migrationBuilder.DropTable(
                name: "zz_deprecated_risk_function");

            migrationBuilder.DropTable(
                name: "zz_deprecated_risk_to_additional_stakeholder");

            migrationBuilder.DropTable(
                name: "zz_deprecated_risk_to_location");

            migrationBuilder.DropTable(
                name: "zz_deprecated_risk_to_technology");

            migrationBuilder.DropTable(
                name: "zz_deprecated_test_status");

            migrationBuilder.DropTable(
                name: "zz_deprecated_threat_catalog");

            migrationBuilder.DropTable(
                name: "zz_deprecated_threat_grouping");

            migrationBuilder.DropTable(
                name: "zz_deprecated_user_pass_history");

            migrationBuilder.DropColumn(
                name: "regulation",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "risks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible by design: the deprecated tables and orphan columns are gone. Restore from the
            // pre-phase backup/dump if recovery is required.
        }
    }
}
