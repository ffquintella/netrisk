START TRANSACTION;
-- Track 6 — Phase 6a: deprecate 23 dead tables (zero references outside DAL/Migrations per the plan census).
-- RENAME to zz_deprecated_* rather than DROP: data is fully preserved and any forgotten code path fails loud
-- and fast. The tables are dropped in Phase 6b (73.sql) after the recorded observation window.
-- Also note (no DDL here): the orphan columns risks.regulation and risks.project_id are unmapped from the EF
-- model this phase but left physically in the DB; they are dropped in 73.sql. The legacy text column
-- risks.status is intentionally NOT deprecated here — it must coexist with the Phase 5 status_id for one
-- release before removal (never dropped in the same release that introduced its replacement).
RENAME TABLE `contributing_risks_impact` TO `zz_deprecated_contributing_risks_impact`;
RENAME TABLE `contributing_risks_likelihood` TO `zz_deprecated_contributing_risks_likelihood`;
RENAME TABLE `control_phase` TO `zz_deprecated_control_phase`;
RENAME TABLE `control_type` TO `zz_deprecated_control_type`;
RENAME TABLE `failed_login_attempts` TO `zz_deprecated_failed_login_attempts`;
RENAME TABLE `file_type_extensions` TO `zz_deprecated_file_type_extensions`;
RENAME TABLE `framework_control_test_audits` TO `zz_deprecated_framework_control_test_audits`;
RENAME TABLE `framework_control_test_comments` TO `zz_deprecated_framework_control_test_comments`;
RENAME TABLE `framework_control_test_results_to_risks` TO `zz_deprecated_framework_control_test_results_to_risks`;
RENAME TABLE `framework_control_type_mappings` TO `zz_deprecated_framework_control_type_mappings`;
RENAME TABLE `mitigation_accept_users` TO `zz_deprecated_mitigation_accept_users`;
RENAME TABLE `permission_to_permission_group` TO `zz_deprecated_permission_to_permission_group`;
RENAME TABLE `questionnaire_pending_risks` TO `zz_deprecated_questionnaire_pending_risks`;
RENAME TABLE `regulation` TO `zz_deprecated_regulation`;
RENAME TABLE `residual_risk_scoring_history` TO `zz_deprecated_residual_risk_scoring_history`;
RENAME TABLE `risk_function` TO `zz_deprecated_risk_function`;
RENAME TABLE `risk_to_additional_stakeholder` TO `zz_deprecated_risk_to_additional_stakeholder`;
RENAME TABLE `risk_to_location` TO `zz_deprecated_risk_to_location`;
RENAME TABLE `risk_to_technology` TO `zz_deprecated_risk_to_technology`;
RENAME TABLE `test_status` TO `zz_deprecated_test_status`;
RENAME TABLE `threat_catalog` TO `zz_deprecated_threat_catalog`;
RENAME TABLE `threat_grouping` TO `zz_deprecated_threat_grouping`;
RENAME TABLE `user_pass_history` TO `zz_deprecated_user_pass_history`;

COMMIT;
