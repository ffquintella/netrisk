-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 6 — Phase 6a: deprecate 23 dead tables (zero references outside DAL/Migrations per the plan census).
-- RENAME to zz_deprecated_* rather than DROP: data is fully preserved and any forgotten code path fails loud
-- and fast. The tables are dropped in Phase 6b (73.sql) after the recorded observation window.
-- Also note (no DDL here): the orphan columns risks.regulation and risks.project_id are unmapped from the EF
-- model this phase but left physically in the DB; they are dropped in 73.sql. The legacy text column
-- risks.status is intentionally NOT deprecated here — it must coexist with the Phase 5 status_id for one
-- release before removal (never dropped in the same release that introduced its replacement).
-- Guarded so re-running this version converges: `zz_deprecated_contributing_risks_impact` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_contributing_risks_impact') > 0,
                 'DO 0', 'RENAME TABLE `contributing_risks_impact` TO `zz_deprecated_contributing_risks_impact`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_contributing_risks_likelihood` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_contributing_risks_likelihood') > 0,
                 'DO 0', 'RENAME TABLE `contributing_risks_likelihood` TO `zz_deprecated_contributing_risks_likelihood`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_control_phase` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_control_phase') > 0,
                 'DO 0', 'RENAME TABLE `control_phase` TO `zz_deprecated_control_phase`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_control_type` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_control_type') > 0,
                 'DO 0', 'RENAME TABLE `control_type` TO `zz_deprecated_control_type`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_failed_login_attempts` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_failed_login_attempts') > 0,
                 'DO 0', 'RENAME TABLE `failed_login_attempts` TO `zz_deprecated_failed_login_attempts`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_file_type_extensions` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_file_type_extensions') > 0,
                 'DO 0', 'RENAME TABLE `file_type_extensions` TO `zz_deprecated_file_type_extensions`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_framework_control_test_audits` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_framework_control_test_audits') > 0,
                 'DO 0', 'RENAME TABLE `framework_control_test_audits` TO `zz_deprecated_framework_control_test_audits`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_framework_control_test_comments` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_framework_control_test_comments') > 0,
                 'DO 0', 'RENAME TABLE `framework_control_test_comments` TO `zz_deprecated_framework_control_test_comments`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_framework_control_test_results_to_risks` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_framework_control_test_results_to_risks') > 0,
                 'DO 0', 'RENAME TABLE `framework_control_test_results_to_risks` TO `zz_deprecated_framework_control_test_results_to_risks`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_framework_control_type_mappings` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_framework_control_type_mappings') > 0,
                 'DO 0', 'RENAME TABLE `framework_control_type_mappings` TO `zz_deprecated_framework_control_type_mappings`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_mitigation_accept_users` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_mitigation_accept_users') > 0,
                 'DO 0', 'RENAME TABLE `mitigation_accept_users` TO `zz_deprecated_mitigation_accept_users`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_permission_to_permission_group` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_permission_to_permission_group') > 0,
                 'DO 0', 'RENAME TABLE `permission_to_permission_group` TO `zz_deprecated_permission_to_permission_group`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_questionnaire_pending_risks` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_questionnaire_pending_risks') > 0,
                 'DO 0', 'RENAME TABLE `questionnaire_pending_risks` TO `zz_deprecated_questionnaire_pending_risks`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_regulation` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_regulation') > 0,
                 'DO 0', 'RENAME TABLE `regulation` TO `zz_deprecated_regulation`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_residual_risk_scoring_history` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_residual_risk_scoring_history') > 0,
                 'DO 0', 'RENAME TABLE `residual_risk_scoring_history` TO `zz_deprecated_residual_risk_scoring_history`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_risk_function` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_risk_function') > 0,
                 'DO 0', 'RENAME TABLE `risk_function` TO `zz_deprecated_risk_function`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_risk_to_additional_stakeholder` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_risk_to_additional_stakeholder') > 0,
                 'DO 0', 'RENAME TABLE `risk_to_additional_stakeholder` TO `zz_deprecated_risk_to_additional_stakeholder`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_risk_to_location` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_risk_to_location') > 0,
                 'DO 0', 'RENAME TABLE `risk_to_location` TO `zz_deprecated_risk_to_location`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_risk_to_technology` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_risk_to_technology') > 0,
                 'DO 0', 'RENAME TABLE `risk_to_technology` TO `zz_deprecated_risk_to_technology`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_test_status` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_test_status') > 0,
                 'DO 0', 'RENAME TABLE `test_status` TO `zz_deprecated_test_status`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_threat_catalog` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_threat_catalog') > 0,
                 'DO 0', 'RENAME TABLE `threat_catalog` TO `zz_deprecated_threat_catalog`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_threat_grouping` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_threat_grouping') > 0,
                 'DO 0', 'RENAME TABLE `threat_grouping` TO `zz_deprecated_threat_grouping`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
-- Guarded so re-running this version converges: `zz_deprecated_user_pass_history` already exists once the rename has run.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'zz_deprecated_user_pass_history') > 0,
                 'DO 0', 'RENAME TABLE `user_pass_history` TO `zz_deprecated_user_pass_history`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

