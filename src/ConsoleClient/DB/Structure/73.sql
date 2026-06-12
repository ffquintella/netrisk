START TRANSACTION;
-- Track 6 — Phase 6b: DESTRUCTIVE drop of the tables deprecated in Phase 6a, after the observation window
-- recorded in schema_upgrade_log (gated by the upgrade tool: requiresPhase 6a + observationDays + --yes).
-- The upgrade tool takes an automatic backup before applying; that dump is the only recovery path.
-- FK checks are disabled around the drops because some deprecated tables reference each other / live tables;
-- the rename in 6a kept those constraints, so they must be ignored to drop in any order.
SET FOREIGN_KEY_CHECKS = 0;

DROP TABLE IF EXISTS `zz_deprecated_contributing_risks_impact`;
DROP TABLE IF EXISTS `zz_deprecated_contributing_risks_likelihood`;
DROP TABLE IF EXISTS `zz_deprecated_control_phase`;
DROP TABLE IF EXISTS `zz_deprecated_control_type`;
DROP TABLE IF EXISTS `zz_deprecated_failed_login_attempts`;
DROP TABLE IF EXISTS `zz_deprecated_file_type_extensions`;
DROP TABLE IF EXISTS `zz_deprecated_framework_control_test_audits`;
DROP TABLE IF EXISTS `zz_deprecated_framework_control_test_comments`;
DROP TABLE IF EXISTS `zz_deprecated_framework_control_test_results_to_risks`;
DROP TABLE IF EXISTS `zz_deprecated_framework_control_type_mappings`;
DROP TABLE IF EXISTS `zz_deprecated_mitigation_accept_users`;
DROP TABLE IF EXISTS `zz_deprecated_permission_to_permission_group`;
DROP TABLE IF EXISTS `zz_deprecated_questionnaire_pending_risks`;
DROP TABLE IF EXISTS `zz_deprecated_regulation`;
DROP TABLE IF EXISTS `zz_deprecated_residual_risk_scoring_history`;
DROP TABLE IF EXISTS `zz_deprecated_risk_function`;
DROP TABLE IF EXISTS `zz_deprecated_risk_to_additional_stakeholder`;
DROP TABLE IF EXISTS `zz_deprecated_risk_to_location`;
DROP TABLE IF EXISTS `zz_deprecated_risk_to_technology`;
DROP TABLE IF EXISTS `zz_deprecated_test_status`;
DROP TABLE IF EXISTS `zz_deprecated_threat_catalog`;
DROP TABLE IF EXISTS `zz_deprecated_threat_grouping`;
DROP TABLE IF EXISTS `zz_deprecated_user_pass_history`;

SET FOREIGN_KEY_CHECKS = 1;

-- Finally drop the orphan columns unmapped in Phase 6a (their legacy indexes drop with them). The legacy
-- risks.status text column is NOT dropped here — its Phase 5 replacement (status_id) must coexist for a
-- release first; its removal belongs to a future phase, not this milestone.
ALTER TABLE `risks` DROP COLUMN `regulation`;
ALTER TABLE `risks` DROP COLUMN `project_id`;

COMMIT;
