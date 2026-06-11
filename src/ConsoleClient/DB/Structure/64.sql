START TRANSACTION;
-- Track 6 — Phase 1: safe fixes (index typo renames + illegal 0000-00-00 default removal).
ALTER TABLE `IncidentResponsePlanTasks` RENAME INDEX `idx_irpt_sequencial` TO `idx_irpt_sequential`;

ALTER TABLE `IncidentResponsePlanTasks` RENAME INDEX `idx_irpt_optinal` TO `idx_irpt_optional`;

ALTER TABLE `BiometricTransaction` RENAME INDEX `idx_biometic_id` TO `idx_biometric_transaction_id`;

ALTER TABLE `BiometricTransaction` RENAME INDEX `idx_biometic_anchor` TO `idx_biometric_transaction_anchor`;

ALTER TABLE `mitigations` MODIFY COLUMN `last_update` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP;

ALTER TABLE `mgmt_reviews` MODIFY COLUMN `next_review` date NOT NULL;

COMMIT;
