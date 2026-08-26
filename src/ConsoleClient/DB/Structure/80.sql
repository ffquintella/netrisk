-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 8 (Risk Governance & Approval Workflows), governance core.
--
-- Purely additive: three new tables, columns on seven existing ones, and nothing is dropped,
-- renamed or retyped. An installation that never opens the governance screens carries three empty
-- tables and behaves exactly as before.
--
-- What arrives here, by milestone:
--   8.1  risk_acceptances gains risk_id / requested_by_id / start_date / renewed_from_id, so one
--        table serves risk-level acceptance as well as the finding-level acceptance Track 3 added.
--   8.2  risk_scoring.residual_risk (+ residual_updated_at) and risk_scoring_history.residual_risk:
--        the post-treatment score, historized beside the inherent one.
--   8.3  risk_appetites, and second_reviewer_id / second_review_at / requires_countersignature /
--        segregation_override_reason on mgmt_reviews.
--   8.4  audit_logs -- one row per changed field on the governance aggregate.
--   8.5  mitigation_tasks, and the triage columns on the previously dead pending_risks table.
--   8.6  risks.business_rank, denormalized from the portal's campaign items so the desktop list can
--        sort on it (the portal's own three tables arrive in version 81).
--   8.7  the quant_* columns on risk_scoring and definition/bounds on likelihood and impact.
--
-- start_date is NOT NULL with a floor default rather than nullable: an acceptance whose start date
-- is unknown is not a thing this schema should be able to represent. Existing finding-level rows
-- are backfilled from created_at by the Data script, which is where DML belongs.
--
-- Every table follows the Track 6 convention (snake_case plural names, fk_/idx_/uq_ prefixes,
-- int-backed enums, tinyint(1) booleans, UTC datetimes, varchar/text and never BLOB for text),
-- because new schema is expected to be born compliant rather than added to the drift.

ALTER TABLE `risks` ADD COLUMN IF NOT EXISTS `business_rank` int(11) NULL;

ALTER TABLE `risks` ADD COLUMN IF NOT EXISTS `review_requested` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `risks` ADD COLUMN IF NOT EXISTS `review_requested_at` datetime NULL;

ALTER TABLE `risks` ADD COLUMN IF NOT EXISTS `review_requested_reason` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `risk_scoring_history` ADD COLUMN IF NOT EXISTS `residual_risk` float NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `quant_ale_mean` double NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `quant_ale_p10` double NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `quant_ale_p50` double NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `quant_ale_p90` double NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `quant_computed_at` datetime NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `quant_lef_max` double NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `quant_lef_min` double NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `quant_lef_most_likely` double NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `quant_loss_exceedance_curve` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `quant_loss_max` double NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `quant_loss_min` double NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `quant_loss_most_likely` double NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `quant_residual_ale_p10` double NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `quant_residual_ale_p50` double NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `quant_residual_ale_p90` double NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `quant_seed` int(11) NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `residual_risk` float NULL;

ALTER TABLE `risk_scoring` ADD COLUMN IF NOT EXISTS `residual_updated_at` datetime NULL;

ALTER TABLE `risk_acceptances` ADD COLUMN IF NOT EXISTS `renewed_from_id` int(11) NULL;

ALTER TABLE `risk_acceptances` ADD COLUMN IF NOT EXISTS `requested_by_id` int(11) NULL;

ALTER TABLE `risk_acceptances` ADD COLUMN IF NOT EXISTS `risk_id` int(11) NULL;

ALTER TABLE `risk_acceptances` ADD COLUMN IF NOT EXISTS `start_date` datetime NOT NULL DEFAULT '1000-01-01 00:00:00';

ALTER TABLE `pending_risks` ADD COLUMN IF NOT EXISTS `dismissal_reason` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `pending_risks` ADD COLUMN IF NOT EXISTS `promoted_risk_id` int(11) NULL;

ALTER TABLE `pending_risks` ADD COLUMN IF NOT EXISTS `status` int(11) NOT NULL DEFAULT 1;

ALTER TABLE `pending_risks` ADD COLUMN IF NOT EXISTS `triaged_at` datetime NULL;

ALTER TABLE `pending_risks` ADD COLUMN IF NOT EXISTS `triaged_by_id` int(11) NULL;

ALTER TABLE `mgmt_reviews` ADD COLUMN IF NOT EXISTS `requires_countersignature` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `mgmt_reviews` ADD COLUMN IF NOT EXISTS `second_review_at` datetime NULL;

ALTER TABLE `mgmt_reviews` ADD COLUMN IF NOT EXISTS `second_reviewer_id` int(11) NULL;

ALTER TABLE `mgmt_reviews` ADD COLUMN IF NOT EXISTS `segregation_override_reason` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `likelihood` ADD COLUMN IF NOT EXISTS `definition` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `likelihood` ADD COLUMN IF NOT EXISTS `probability_max` double NULL;

ALTER TABLE `likelihood` ADD COLUMN IF NOT EXISTS `probability_min` double NULL;

ALTER TABLE `impact` ADD COLUMN IF NOT EXISTS `definition` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `impact` ADD COLUMN IF NOT EXISTS `impact_max` double NULL;

ALTER TABLE `impact` ADD COLUMN IF NOT EXISTS `impact_min` double NULL;

CREATE TABLE IF NOT EXISTS `audit_logs` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `entity_type` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `entity_id` int(11) NOT NULL,
    `field` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `old_value` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `new_value` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `action` int(11) NOT NULL,
    `user_id` int(11) NULL,
    `actor` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `occurred_at` datetime NOT NULL,
    `correlation_id` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_audit_logs_user_id` FOREIGN KEY (`user_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `mitigation_tasks` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `mitigation_id` int(11) NOT NULL,
    `title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `description` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `owner_id` int(11) NULL,
    `due_date` datetime NULL,
    `status` int(11) NOT NULL DEFAULT 1,
    `completed_at` datetime NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    `created_by_id` int(11) NULL,
    `last_notified_days_before` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_mitigation_tasks_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_mitigation_tasks_mitigation_id` FOREIGN KEY (`mitigation_id`) REFERENCES `mitigations` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_mitigation_tasks_owner_id` FOREIGN KEY (`owner_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_appetites` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `entity_id` int(11) NULL,
    `max_acceptable_residual` double NOT NULL,
    `dual_approval_threshold` double NOT NULL,
    `notes` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_risk_appetites_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_appetites_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_risks_business_rank` ON `risks` (`business_rank`);

CREATE INDEX IF NOT EXISTS `idx_risks_review_requested` ON `risks` (`review_requested`);

CREATE INDEX IF NOT EXISTS `idx_risk_scoring_residual_risk` ON `risk_scoring` (`residual_risk`);

CREATE INDEX IF NOT EXISTS `idx_ra_renewed_from_id` ON `risk_acceptances` (`renewed_from_id`);

CREATE INDEX IF NOT EXISTS `idx_ra_risk_id` ON `risk_acceptances` (`risk_id`);

CREATE INDEX IF NOT EXISTS `IX_risk_acceptances_requested_by_id` ON `risk_acceptances` (`requested_by_id`);

CREATE INDEX IF NOT EXISTS `idx_pending_risks_status` ON `pending_risks` (`status`);

CREATE INDEX IF NOT EXISTS `idx_mgmt_reviews_second_reviewer_id` ON `mgmt_reviews` (`second_reviewer_id`);

CREATE INDEX IF NOT EXISTS `idx_audit_logs_correlation_id` ON `audit_logs` (`correlation_id`);

CREATE INDEX IF NOT EXISTS `idx_audit_logs_entity_type_entity_id` ON `audit_logs` (`entity_type`, `entity_id`);

CREATE INDEX IF NOT EXISTS `idx_audit_logs_occurred_at` ON `audit_logs` (`occurred_at`);

CREATE INDEX IF NOT EXISTS `IX_audit_logs_user_id` ON `audit_logs` (`user_id`);

CREATE INDEX IF NOT EXISTS `idx_mitigation_tasks_mitigation_id` ON `mitigation_tasks` (`mitigation_id`);

CREATE INDEX IF NOT EXISTS `idx_mitigation_tasks_owner_id` ON `mitigation_tasks` (`owner_id`);

CREATE INDEX IF NOT EXISTS `idx_mitigation_tasks_status_due_date` ON `mitigation_tasks` (`status`, `due_date`);

CREATE INDEX IF NOT EXISTS `IX_mitigation_tasks_created_by_id` ON `mitigation_tasks` (`created_by_id`);

CREATE INDEX IF NOT EXISTS `IX_risk_appetites_created_by_id` ON `risk_appetites` (`created_by_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_appetites_entity_id` ON `risk_appetites` (`entity_id`);

ALTER TABLE `mgmt_reviews`
    ADD CONSTRAINT `fk_mgmt_reviews_second_reviewer_id` FOREIGN KEY IF NOT EXISTS (`second_reviewer_id`) REFERENCES `user` (`value`) ON DELETE RESTRICT;

ALTER TABLE `risk_acceptances`
    ADD CONSTRAINT `fk_ra_renewed_from_id` FOREIGN KEY IF NOT EXISTS (`renewed_from_id`) REFERENCES `risk_acceptances` (`id`) ON DELETE RESTRICT;

ALTER TABLE `risk_acceptances`
    ADD CONSTRAINT `fk_ra_requested_by_id` FOREIGN KEY IF NOT EXISTS (`requested_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL;

ALTER TABLE `risk_acceptances`
    ADD CONSTRAINT `fk_ra_risk_id` FOREIGN KEY IF NOT EXISTS (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE;
