-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 9 Stage 9.12 (M50, T211-T215, S53) -- AI governance: the model inventory, flag 11 and the
-- model metrics.
--
--   ai_models                  The inventory: purpose, kind, source and vendor (a third party),
--                              version, status, risk tier, human oversight, owner, unit, and how long
--                              an evaluation stays current. Retired with a reason, never deleted.
--   ai_model_data_links        The data records a model uses, and how -- the organizationData nodes
--                              the Stage 9.11 catalogue is keyed by (no column on any existing link).
--   ai_model_metric_readings   The insert-only evaluation history: accuracy, precision, recall,
--                              calibration, drift and the human override rate, each of a version.
--   ai_model_overrides         A person's decision contrary to the model, with author and reason --
--                              what the override rate is computed from.
--   ai_model_risks             The register's risks that involve a model; a link to a model that is
--                              not retired derives flag 11.
--
-- Governance, never use: nothing here lets a model act, and no column holds a credential.
--
-- New tables only -- no ALTER on an existing table. Created in reference order: ai_models
-- (third_parties, entities, user) before everything that references it. The readings, overrides
-- and risk links reference a model with ON DELETE RESTRICT: the evaluation history is evidence and
-- a model is retired, never deleted (S53 D9).
--
-- Version 101 rather than the 101/102 S40 section 7.6 had reserved for M52 and M56/M57: this change
-- merged first, so S40 shifts its scripts to 102 and 103 (the rule #79, #80 and Stages 9.1-9.11 followed).

CREATE TABLE IF NOT EXISTS `ai_models` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `name` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `purpose` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `kind` int(11) NOT NULL,
    `source` int(11) NOT NULL,
    `third_party_id` int(11) NULL,
    `version` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `version_since` datetime NULL,
    `status` int(11) NOT NULL,
    `risk_tier` int(11) NULL,
    `human_oversight` int(11) NULL,
    `owner_id` int(11) NULL,
    `entity_id` int(11) NULL,
    `max_evaluation_age_days` int(11) NOT NULL,
    `data_declared_at` datetime NULL,
    `notes` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `retired_at` datetime NULL,
    `retired_by_id` int(11) NULL,
    `retire_reason` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    `updated_at` datetime NULL,
    `updated_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_ai_models_human_oversight` CHECK (`human_oversight` IS NULL OR (`human_oversight` >= 1 AND `human_oversight` <= 3)),
    CONSTRAINT `ck_ai_models_kind` CHECK (`kind` >= 1 AND `kind` <= 7),
    CONSTRAINT `ck_ai_models_max_evaluation_age_days` CHECK (`max_evaluation_age_days` >= 1 AND `max_evaluation_age_days` <= 1096),
    CONSTRAINT `ck_ai_models_retired` CHECK ((`status` = 4 AND `retired_at` IS NOT NULL AND `retire_reason` IS NOT NULL) OR (`status` <> 4 AND `retired_at` IS NULL)),
    CONSTRAINT `ck_ai_models_risk_tier` CHECK (`risk_tier` IS NULL OR (`risk_tier` >= 1 AND `risk_tier` <= 3)),
    CONSTRAINT `ck_ai_models_source` CHECK (`source` >= 1 AND `source` <= 3),
    CONSTRAINT `ck_ai_models_status` CHECK (`status` >= 1 AND `status` <= 4),
    CONSTRAINT `fk_ai_models_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_ai_models_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `fk_ai_models_owner_id` FOREIGN KEY (`owner_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_ai_models_retired_by_id` FOREIGN KEY (`retired_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_ai_models_third_party_id` FOREIGN KEY (`third_party_id`) REFERENCES `third_parties` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_ai_models_updated_by_id` FOREIGN KEY (`updated_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `ai_model_data_links` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `model_id` int(11) NOT NULL,
    `entity_id` int(11) NOT NULL,
    `data_usage` int(11) NOT NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_ai_model_data_links_data_usage` CHECK (`data_usage` >= 1 AND `data_usage` <= 5),
    CONSTRAINT `fk_ai_model_data_links_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_ai_model_data_links_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `fk_ai_model_data_links_model_id` FOREIGN KEY (`model_id`) REFERENCES `ai_models` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `ai_model_metric_readings` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `model_id` int(11) NOT NULL,
    `metric` int(11) NOT NULL,
    `value` decimal(18,6) NOT NULL,
    `model_version` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `measured_at` datetime NOT NULL,
    `period_start` datetime NULL,
    `period_end` datetime NULL,
    `sample_size` int(11) NULL,
    `override_count` int(11) NULL,
    `method` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `evidence_reference` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `recorded_by_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    `voided_at` datetime NULL,
    `voided_by_id` int(11) NULL,
    `void_reason` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_ai_model_metric_readings_metric` CHECK (`metric` >= 1 AND `metric` <= 6),
    CONSTRAINT `ck_ai_model_metric_readings_override_rate` CHECK ((`metric` = 6 AND `period_start` IS NOT NULL AND `sample_size` IS NOT NULL AND `override_count` IS NOT NULL AND `override_count` >= 0 AND `override_count` <= `sample_size`) OR (`metric` <> 6 AND `override_count` IS NULL)),
    CONSTRAINT `ck_ai_model_metric_readings_period` CHECK ((`period_start` IS NULL AND `period_end` IS NULL) OR (`period_start` IS NOT NULL AND `period_end` IS NOT NULL AND `period_end` > `period_start`)),
    CONSTRAINT `ck_ai_model_metric_readings_sample_size` CHECK (`sample_size` IS NULL OR `sample_size` >= 1),
    CONSTRAINT `ck_ai_model_metric_readings_value` CHECK (`value` >= 0 AND (`metric` = 5 OR `value` <= 1)),
    CONSTRAINT `ck_ai_model_metric_readings_void` CHECK ((`voided_at` IS NULL AND `void_reason` IS NULL) OR (`voided_at` IS NOT NULL AND `void_reason` IS NOT NULL)),
    CONSTRAINT `fk_ai_model_metric_readings_model_id` FOREIGN KEY (`model_id`) REFERENCES `ai_models` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_ai_model_metric_readings_recorded_by_id` FOREIGN KEY (`recorded_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_ai_model_metric_readings_voided_by_id` FOREIGN KEY (`voided_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `ai_model_overrides` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `model_id` int(11) NOT NULL,
    `model_version` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `occurred_at` datetime NOT NULL,
    `model_output` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `human_decision` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `reason` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `recorded_by_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    `voided_at` datetime NULL,
    `voided_by_id` int(11) NULL,
    `void_reason` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_ai_model_overrides_void` CHECK ((`voided_at` IS NULL AND `void_reason` IS NULL) OR (`voided_at` IS NOT NULL AND `void_reason` IS NOT NULL)),
    CONSTRAINT `fk_ai_model_overrides_model_id` FOREIGN KEY (`model_id`) REFERENCES `ai_models` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_ai_model_overrides_recorded_by_id` FOREIGN KEY (`recorded_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_ai_model_overrides_voided_by_id` FOREIGN KEY (`voided_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `ai_model_risks` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `model_id` int(11) NOT NULL,
    `risk_id` int(11) NOT NULL,
    `note` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_ai_model_risks_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_ai_model_risks_model_id` FOREIGN KEY (`model_id`) REFERENCES `ai_models` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_ai_model_risks_risk_id` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_ai_model_data_links_created_by_id` ON `ai_model_data_links` (`created_by_id`);
CREATE INDEX IF NOT EXISTS `idx_ai_model_data_links_entity_id` ON `ai_model_data_links` (`entity_id`);
CREATE UNIQUE INDEX IF NOT EXISTS `uq_ai_model_data_links_model_id_entity_id_data_usage` ON `ai_model_data_links` (`model_id`, `entity_id`, `data_usage`);
CREATE INDEX IF NOT EXISTS `idx_ai_model_metric_readings_model_id_metric_measured_at` ON `ai_model_metric_readings` (`model_id`, `metric`, `measured_at`);
CREATE INDEX IF NOT EXISTS `idx_ai_model_metric_readings_recorded_by_id` ON `ai_model_metric_readings` (`recorded_by_id`);
CREATE INDEX IF NOT EXISTS `idx_ai_model_metric_readings_voided_by_id` ON `ai_model_metric_readings` (`voided_by_id`);
CREATE INDEX IF NOT EXISTS `idx_ai_model_overrides_model_id_occurred_at` ON `ai_model_overrides` (`model_id`, `occurred_at`);
CREATE INDEX IF NOT EXISTS `idx_ai_model_overrides_recorded_by_id` ON `ai_model_overrides` (`recorded_by_id`);
CREATE INDEX IF NOT EXISTS `idx_ai_model_overrides_voided_by_id` ON `ai_model_overrides` (`voided_by_id`);
CREATE INDEX IF NOT EXISTS `idx_ai_model_risks_created_by_id` ON `ai_model_risks` (`created_by_id`);
CREATE INDEX IF NOT EXISTS `idx_ai_model_risks_risk_id` ON `ai_model_risks` (`risk_id`);
CREATE UNIQUE INDEX IF NOT EXISTS `uq_ai_model_risks_model_id_risk_id` ON `ai_model_risks` (`model_id`, `risk_id`);
CREATE INDEX IF NOT EXISTS `idx_ai_models_created_by_id` ON `ai_models` (`created_by_id`);
CREATE INDEX IF NOT EXISTS `idx_ai_models_entity_id` ON `ai_models` (`entity_id`);
CREATE INDEX IF NOT EXISTS `idx_ai_models_owner_id` ON `ai_models` (`owner_id`);
CREATE INDEX IF NOT EXISTS `idx_ai_models_retired_by_id` ON `ai_models` (`retired_by_id`);
CREATE INDEX IF NOT EXISTS `idx_ai_models_status` ON `ai_models` (`status`);
CREATE INDEX IF NOT EXISTS `idx_ai_models_third_party_id` ON `ai_models` (`third_party_id`);
CREATE INDEX IF NOT EXISTS `idx_ai_models_updated_by_id` ON `ai_models` (`updated_by_id`);
CREATE UNIQUE INDEX IF NOT EXISTS `uq_ai_models_name` ON `ai_models` (`name`);
