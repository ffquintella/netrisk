-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 6 — Phase 1: safe fixes (index typo renames + illegal 0000-00-00 default removal).
-- Guarded so re-running this version converges: the index is already named `idx_irpt_sequential`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'IncidentResponsePlanTasks' AND BINARY INDEX_NAME = 'idx_irpt_sequential') > 0,
                 'DO 0', 'ALTER TABLE `IncidentResponsePlanTasks` RENAME INDEX `idx_irpt_sequencial` TO `idx_irpt_sequential`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `idx_irpt_optional`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'IncidentResponsePlanTasks' AND BINARY INDEX_NAME = 'idx_irpt_optional') > 0,
                 'DO 0', 'ALTER TABLE `IncidentResponsePlanTasks` RENAME INDEX `idx_irpt_optinal` TO `idx_irpt_optional`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `idx_biometric_transaction_id`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'BiometricTransaction' AND BINARY INDEX_NAME = 'idx_biometric_transaction_id') > 0,
                 'DO 0', 'ALTER TABLE `BiometricTransaction` RENAME INDEX `idx_biometic_id` TO `idx_biometric_transaction_id`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `idx_biometric_transaction_anchor`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'BiometricTransaction' AND BINARY INDEX_NAME = 'idx_biometric_transaction_anchor') > 0,
                 'DO 0', 'ALTER TABLE `BiometricTransaction` RENAME INDEX `idx_biometic_anchor` TO `idx_biometric_transaction_anchor`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

ALTER TABLE `mitigations` MODIFY COLUMN `last_update` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP;

ALTER TABLE `mgmt_reviews` MODIFY COLUMN `next_review` date NOT NULL;

