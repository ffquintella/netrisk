-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 9 Stage 9.6 (M44, T174-T181, S47) -- treatment economics: monetary cost, Gates C and D,
-- the four treatment options, the target risk level and the Phase 5 action-plan evidence.
--
--   mitigation_economics     One row per mitigation: the typed treatment option (avoid, reduce,
--                            transfer/share, accept) and the monetary cost declared as a block
--                            (one-time, annual, annual side effects, amortization horizon), beside the
--                            untouched ordinal mitigation_cost scale; effort and duration for Gate D.
--   mitigation_dependencies  "This treatment depends on that one" -- Gate D's dependency constraint.
--   risk_targets             The target risk level of the register: score (0-10) and/or annual E[L].
--   mitigation_tasks         + acceptance criterion and completion evidence (who recorded it, when).
--   risk_scoring             + quant_residual_ale_mean, Gate C's E[L] after (the mean, never the median).
--
-- Version 95 rather than the 95/96 S40 section 7.6 had reserved for M52 and M56/M57: this change
-- merged first, so S40 shifts its scripts to 96 and 97 (the rule #79, #80 and Stages 9.1-9.5 followed).

CREATE TABLE IF NOT EXISTS `mitigation_economics` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `mitigation_id` int(11) NOT NULL,
    `treatment_option` int(11) NOT NULL,
    `transfer_counterparty` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `cost_one_time` decimal(18,2) NULL,
    `cost_annual` decimal(18,2) NULL,
    `cost_side_effects_annual` decimal(18,2) NULL,
    `cost_horizon_years` int(11) NULL,
    `cost_basis` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `effort_person_days` decimal(10,2) NULL,
    `duration_days` int(11) NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    `updated_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_mitigation_economics_cost_complete` CHECK ((`cost_one_time` IS NULL) = (`cost_annual` IS NULL) AND (`cost_annual` IS NULL) = (`cost_side_effects_annual` IS NULL)),
    CONSTRAINT `ck_mitigation_economics_cost_non_negative` CHECK ((`cost_one_time` IS NULL OR `cost_one_time` >= 0) AND (`cost_annual` IS NULL OR `cost_annual` >= 0) AND (`cost_side_effects_annual` IS NULL OR `cost_side_effects_annual` >= 0)),
    CONSTRAINT `ck_mitigation_economics_horizon` CHECK (`cost_horizon_years` IS NULL OR (`cost_horizon_years` >= 1 AND `cost_horizon_years` <= 30)),
    CONSTRAINT `ck_mitigation_economics_treatment_option` CHECK (`treatment_option` >= 1 AND `treatment_option` <= 4),
    CONSTRAINT `fk_mitigation_economics_mitigation_id` FOREIGN KEY (`mitigation_id`) REFERENCES `mitigations` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_mitigation_economics_updated_by_id` FOREIGN KEY (`updated_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_mitigation_economics_mitigation_id` ON `mitigation_economics` (`mitigation_id`);
CREATE INDEX IF NOT EXISTS `idx_mitigation_economics_updated_by_id` ON `mitigation_economics` (`updated_by_id`);

CREATE TABLE IF NOT EXISTS `mitigation_dependencies` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `mitigation_id` int(11) NOT NULL,
    `prerequisite_id` int(11) NOT NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_mitigation_dependencies_not_self` CHECK (`mitigation_id` <> `prerequisite_id`),
    CONSTRAINT `fk_mitigation_dependencies_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_mitigation_dependencies_mitigation_id` FOREIGN KEY (`mitigation_id`) REFERENCES `mitigations` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_mitigation_dependencies_prerequisite_id` FOREIGN KEY (`prerequisite_id`) REFERENCES `mitigations` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_mitigation_dependencies_mitigation_id_prerequisite_id` ON `mitigation_dependencies` (`mitigation_id`, `prerequisite_id`);
CREATE INDEX IF NOT EXISTS `idx_mitigation_dependencies_prerequisite_id` ON `mitigation_dependencies` (`prerequisite_id`);
CREATE INDEX IF NOT EXISTS `idx_mitigation_dependencies_created_by_id` ON `mitigation_dependencies` (`created_by_id`);

CREATE TABLE IF NOT EXISTS `risk_targets` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `risk_id` int(11) NOT NULL,
    `target_score` decimal(4,2) NULL,
    `target_expected_loss` decimal(18,2) NULL,
    `target_date` date NULL,
    `rationale` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `set_by_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_targets_expected_loss` CHECK (`target_expected_loss` IS NULL OR `target_expected_loss` >= 0),
    CONSTRAINT `ck_risk_targets_level` CHECK (`target_score` IS NOT NULL OR `target_expected_loss` IS NOT NULL),
    CONSTRAINT `ck_risk_targets_score` CHECK (`target_score` IS NULL OR (`target_score` >= 0 AND `target_score` <= 10)),
    CONSTRAINT `fk_risk_targets_risk_id` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_risk_targets_set_by_id` FOREIGN KEY (`set_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_targets_risk_id` ON `risk_targets` (`risk_id`);
CREATE INDEX IF NOT EXISTS `idx_risk_targets_set_by_id` ON `risk_targets` (`set_by_id`);

ALTER TABLE `mitigation_tasks` ADD COLUMN IF NOT EXISTS `acceptance_criterion` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `mitigation_tasks` ADD COLUMN IF NOT EXISTS `completion_evidence` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `mitigation_tasks` ADD COLUMN IF NOT EXISTS `completion_evidence_at` datetime NULL;

ALTER TABLE `mitigation_tasks` ADD COLUMN IF NOT EXISTS `completion_evidence_by_id` int(11) NULL;

CREATE INDEX IF NOT EXISTS `idx_mitigation_tasks_completion_evidence_by_id` ON `mitigation_tasks` (`completion_evidence_by_id`);

ALTER TABLE `mitigation_tasks` ADD CONSTRAINT `fk_mitigation_tasks_completion_evidence_by_id` FOREIGN KEY IF NOT EXISTS (`completion_evidence_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `quant_residual_ale_mean` double NULL;
