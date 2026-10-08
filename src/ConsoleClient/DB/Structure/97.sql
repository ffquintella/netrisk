-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 9 Stage 9.8 (M46, T188-T193, S49) -- KRIs, the mandatory reassessment triggers of Phase 7 and the
-- methodology's metrics.
--
--   kris                        A key risk indicator: what it measures, its source, the Phase 0 tolerance with the
--                               decision that set it, which way it gets worse and how old its latest reading may be
--                               before it reads stale. Retired, never deleted.
--   kri_readings                The indicator's history. Insert-only; a mistaken reading is voided with a reason.
--   kri_risks                   The risks an indicator governs: it enters their Gate B and triggers their reassessment.
--   reassessment_events         One of the six Phase 7 triggers -- declared by a person, or a KRI breach episode
--                               (one row per episode, unique on the reading that opened it; one per incident).
--   risk_reassessment_triggers  An event applied to one risk, once (unique on event and risk).
--
-- New tables only. Created in reference order: kris before kri_readings and kri_risks, those before
-- reassessment_events, that before risk_reassessment_triggers.
--
-- Version 97 rather than the 97/98 S40 section 7.6 had reserved for M52 and M56/M57: this change
-- merged first, so S40 shifts its scripts to 98 and 99 (the rule #79, #80 and Stages 9.1-9.7 followed).

CREATE TABLE IF NOT EXISTS `kris` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `name` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `description` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `category` int(11) NOT NULL,
    `source` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `unit` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `direction` int(11) NOT NULL,
    `tolerance_threshold` decimal(18,4) NOT NULL,
    `warning_threshold` decimal(18,4) NULL,
    `tolerance_rationale` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `max_reading_age_days` int(11) NOT NULL,
    `owner_id` int(11) NULL,
    `entity_id` int(11) NULL,
    `retired_at` datetime NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    `updated_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_kris_category` CHECK (`category` >= 1 AND `category` <= 4),
    CONSTRAINT `ck_kris_direction` CHECK (`direction` >= 1 AND `direction` <= 2),
    CONSTRAINT `ck_kris_max_reading_age_days` CHECK (`max_reading_age_days` >= 1 AND `max_reading_age_days` <= 366),
    CONSTRAINT `ck_kris_warning_side` CHECK (`warning_threshold` IS NULL OR (`direction` = 1 AND `warning_threshold` < `tolerance_threshold`) OR (`direction` = 2 AND `warning_threshold` > `tolerance_threshold`)),
    CONSTRAINT `fk_kris_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `fk_kris_owner_id` FOREIGN KEY (`owner_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_kris_updated_by_id` FOREIGN KEY (`updated_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `kri_readings` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `kri_id` int(11) NOT NULL,
    `value` decimal(18,4) NOT NULL,
    `observed_at` datetime NOT NULL,
    `note` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `recorded_by_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    `voided_at` datetime NULL,
    `voided_by_id` int(11) NULL,
    `void_reason` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_kri_readings_void` CHECK ((`voided_at` IS NULL AND `void_reason` IS NULL) OR (`voided_at` IS NOT NULL AND `void_reason` IS NOT NULL)),
    CONSTRAINT `fk_kri_readings_kri_id` FOREIGN KEY (`kri_id`) REFERENCES `kris` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_kri_readings_recorded_by_id` FOREIGN KEY (`recorded_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_kri_readings_voided_by_id` FOREIGN KEY (`voided_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `kri_risks` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `kri_id` int(11) NOT NULL,
    `risk_id` int(11) NOT NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_kri_risks_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_kri_risks_kri_id` FOREIGN KEY (`kri_id`) REFERENCES `kris` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_kri_risks_risk_id` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `reassessment_events` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `trigger_type` int(11) NOT NULL,
    `origin` int(11) NOT NULL,
    `title` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `description` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `occurred_at` datetime NOT NULL,
    `incident_id` int(11) NULL,
    `kri_id` int(11) NULL,
    `kri_reading_id` int(11) NULL,
    `kri_breach_ended_at` datetime NULL,
    `declared_by_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_reassessment_events_incident_type` CHECK (`incident_id` IS NULL OR `trigger_type` = 3),
    CONSTRAINT `ck_reassessment_events_kri_origin` CHECK ((`origin` = 2 AND `kri_id` IS NOT NULL AND `kri_reading_id` IS NOT NULL AND `trigger_type` = 6) OR (`origin` = 1 AND `kri_id` IS NULL AND `kri_reading_id` IS NULL AND `kri_breach_ended_at` IS NULL)),
    CONSTRAINT `ck_reassessment_events_origin` CHECK (`origin` >= 1 AND `origin` <= 2),
    CONSTRAINT `ck_reassessment_events_trigger_type` CHECK (`trigger_type` >= 1 AND `trigger_type` <= 6),
    CONSTRAINT `fk_reassessment_events_declared_by_id` FOREIGN KEY (`declared_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_reassessment_events_incident_id` FOREIGN KEY (`incident_id`) REFERENCES `incidents` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `fk_reassessment_events_kri_id` FOREIGN KEY (`kri_id`) REFERENCES `kris` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_reassessment_events_kri_reading_id` FOREIGN KEY (`kri_reading_id`) REFERENCES `kri_readings` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_reassessment_triggers` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `event_id` int(11) NOT NULL,
    `risk_id` int(11) NOT NULL,
    `raised_at` datetime NOT NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_risk_reassessment_triggers_event_id` FOREIGN KEY (`event_id`) REFERENCES `reassessment_events` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_risk_reassessment_triggers_risk_id` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_kri_readings_kri_id_observed_at` ON `kri_readings` (`kri_id`, `observed_at`);

CREATE INDEX IF NOT EXISTS `idx_kri_readings_recorded_by_id` ON `kri_readings` (`recorded_by_id`);

CREATE INDEX IF NOT EXISTS `idx_kri_readings_voided_by_id` ON `kri_readings` (`voided_by_id`);

CREATE INDEX IF NOT EXISTS `idx_kri_risks_created_by_id` ON `kri_risks` (`created_by_id`);

CREATE INDEX IF NOT EXISTS `idx_kri_risks_risk_id` ON `kri_risks` (`risk_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_kri_risks_kri_id_risk_id` ON `kri_risks` (`kri_id`, `risk_id`);

CREATE INDEX IF NOT EXISTS `idx_kris_entity_id` ON `kris` (`entity_id`);

CREATE INDEX IF NOT EXISTS `idx_kris_owner_id` ON `kris` (`owner_id`);

CREATE INDEX IF NOT EXISTS `idx_kris_updated_by_id` ON `kris` (`updated_by_id`);

CREATE INDEX IF NOT EXISTS `idx_reassessment_events_declared_by_id` ON `reassessment_events` (`declared_by_id`);

CREATE INDEX IF NOT EXISTS `idx_reassessment_events_kri_id` ON `reassessment_events` (`kri_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_reassessment_events_incident_id` ON `reassessment_events` (`incident_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_reassessment_events_kri_reading_id` ON `reassessment_events` (`kri_reading_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_reassessment_triggers_risk_id` ON `risk_reassessment_triggers` (`risk_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_reassessment_triggers_event_id_risk_id` ON `risk_reassessment_triggers` (`event_id`, `risk_id`);
