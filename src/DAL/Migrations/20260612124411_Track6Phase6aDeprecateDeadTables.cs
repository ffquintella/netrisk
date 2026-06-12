using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Track 6 Phase 6a — deprecate dead tables (reversible). The 23 zero-reference tables are unmapped from
    /// the EF model (DbSets + configurations removed) and RENAMED to <c>zz_deprecated_*</c> here, not dropped:
    /// data is preserved and any forgotten access fails loud and fast. The actual production path is the
    /// numbered SQL (Structure/72.sql); this migration mirrors it (rename, not the scaffolded DropTable) so a
    /// dev <c>dotnet ef database update</c> deprecates rather than destroys. Phase 6b (73.sql) drops them after
    /// the observation window. The orphan columns risks.regulation / risks.project_id are unmapped in the model
    /// (gone from the snapshot) but left physically in the DB here; they are dropped in Phase 6b.
    /// </remarks>
    public partial class Track6Phase6aDeprecateDeadTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "contributing_risks_impact",
                newName: "zz_deprecated_contributing_risks_impact");

            migrationBuilder.RenameTable(
                name: "contributing_risks_likelihood",
                newName: "zz_deprecated_contributing_risks_likelihood");

            migrationBuilder.RenameTable(
                name: "control_phase",
                newName: "zz_deprecated_control_phase");

            migrationBuilder.RenameTable(
                name: "control_type",
                newName: "zz_deprecated_control_type");

            migrationBuilder.RenameTable(
                name: "failed_login_attempts",
                newName: "zz_deprecated_failed_login_attempts");

            migrationBuilder.RenameTable(
                name: "file_type_extensions",
                newName: "zz_deprecated_file_type_extensions");

            migrationBuilder.RenameTable(
                name: "framework_control_test_audits",
                newName: "zz_deprecated_framework_control_test_audits");

            migrationBuilder.RenameTable(
                name: "framework_control_test_comments",
                newName: "zz_deprecated_framework_control_test_comments");

            migrationBuilder.RenameTable(
                name: "framework_control_test_results_to_risks",
                newName: "zz_deprecated_framework_control_test_results_to_risks");

            migrationBuilder.RenameTable(
                name: "framework_control_type_mappings",
                newName: "zz_deprecated_framework_control_type_mappings");

            migrationBuilder.RenameTable(
                name: "mitigation_accept_users",
                newName: "zz_deprecated_mitigation_accept_users");

            migrationBuilder.RenameTable(
                name: "permission_to_permission_group",
                newName: "zz_deprecated_permission_to_permission_group");

            migrationBuilder.RenameTable(
                name: "questionnaire_pending_risks",
                newName: "zz_deprecated_questionnaire_pending_risks");

            migrationBuilder.RenameTable(
                name: "regulation",
                newName: "zz_deprecated_regulation");

            migrationBuilder.RenameTable(
                name: "residual_risk_scoring_history",
                newName: "zz_deprecated_residual_risk_scoring_history");

            migrationBuilder.RenameTable(
                name: "risk_function",
                newName: "zz_deprecated_risk_function");

            migrationBuilder.RenameTable(
                name: "risk_to_additional_stakeholder",
                newName: "zz_deprecated_risk_to_additional_stakeholder");

            migrationBuilder.RenameTable(
                name: "risk_to_location",
                newName: "zz_deprecated_risk_to_location");

            migrationBuilder.RenameTable(
                name: "risk_to_technology",
                newName: "zz_deprecated_risk_to_technology");

            migrationBuilder.RenameTable(
                name: "test_status",
                newName: "zz_deprecated_test_status");

            migrationBuilder.RenameTable(
                name: "threat_catalog",
                newName: "zz_deprecated_threat_catalog");

            migrationBuilder.RenameTable(
                name: "threat_grouping",
                newName: "zz_deprecated_threat_grouping");

            migrationBuilder.RenameTable(
                name: "user_pass_history",
                newName: "zz_deprecated_user_pass_history");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "zz_deprecated_contributing_risks_impact",
                newName: "contributing_risks_impact");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_contributing_risks_likelihood",
                newName: "contributing_risks_likelihood");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_control_phase",
                newName: "control_phase");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_control_type",
                newName: "control_type");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_failed_login_attempts",
                newName: "failed_login_attempts");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_file_type_extensions",
                newName: "file_type_extensions");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_framework_control_test_audits",
                newName: "framework_control_test_audits");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_framework_control_test_comments",
                newName: "framework_control_test_comments");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_framework_control_test_results_to_risks",
                newName: "framework_control_test_results_to_risks");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_framework_control_type_mappings",
                newName: "framework_control_type_mappings");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_mitigation_accept_users",
                newName: "mitigation_accept_users");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_permission_to_permission_group",
                newName: "permission_to_permission_group");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_questionnaire_pending_risks",
                newName: "questionnaire_pending_risks");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_regulation",
                newName: "regulation");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_residual_risk_scoring_history",
                newName: "residual_risk_scoring_history");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_risk_function",
                newName: "risk_function");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_risk_to_additional_stakeholder",
                newName: "risk_to_additional_stakeholder");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_risk_to_location",
                newName: "risk_to_location");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_risk_to_technology",
                newName: "risk_to_technology");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_test_status",
                newName: "test_status");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_threat_catalog",
                newName: "threat_catalog");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_threat_grouping",
                newName: "threat_grouping");

            migrationBuilder.RenameTable(
                name: "zz_deprecated_user_pass_history",
                newName: "user_pass_history");
        }
    }
}
