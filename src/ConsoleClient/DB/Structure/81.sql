-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 8 milestone 8.6 -- the business risk acceptance portal.
--
-- Three tables, all additive:
--   entity_risk_reviewers        who the business appointed to review its risks, per entity
--   risk_review_campaigns        one periodic review per entity per period (quarterly by default)
--   risk_review_campaign_items   one risk inside a campaign, with the reviewer's rank and decision
--
-- Its own version rather than folded into 80: the portal is the last thing in the Track 8 dependency
-- chain and an installation that does not deploy it should be able to see which phase it is
-- declining. uq_risk_review_campaigns_entity_period is what makes the daily generator converge on
-- the same campaign instead of creating a new one every morning.

CREATE TABLE IF NOT EXISTS `entity_risk_reviewers` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `entity_id` int(11) NOT NULL,
    `user_id` int(11) NOT NULL,
    `is_primary` tinyint(1) NOT NULL DEFAULT FALSE,
    `appointed_by_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_entity_risk_reviewers_appointed_by_id` FOREIGN KEY (`appointed_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_entity_risk_reviewers_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `fk_entity_risk_reviewers_user_id` FOREIGN KEY (`user_id`) REFERENCES `user` (`value`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_review_campaigns` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `entity_id` int(11) NOT NULL,
    `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `period_start` datetime NOT NULL,
    `period_end` datetime NOT NULL,
    `due_date` datetime NOT NULL,
    `status` int(11) NOT NULL DEFAULT 1,
    `created_at` datetime NOT NULL,
    `completed_at` datetime NULL,
    `last_notified_days_before` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_risk_review_campaigns_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_review_campaign_items` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `campaign_id` int(11) NOT NULL,
    `risk_id` int(11) NOT NULL,
    `rank` int(11) NULL,
    `decision` int(11) NOT NULL DEFAULT 1,
    `decision_notes` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `decided_by_id` int(11) NULL,
    `decided_at` datetime NULL,
    `risk_acceptance_id` int(11) NULL,
    `escalated_to_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_risk_review_campaign_items_campaign_id` FOREIGN KEY (`campaign_id`) REFERENCES `risk_review_campaigns` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_risk_review_campaign_items_decided_by_id` FOREIGN KEY (`decided_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_review_campaign_items_escalated_to_id` FOREIGN KEY (`escalated_to_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_review_campaign_items_risk_acceptance_id` FOREIGN KEY (`risk_acceptance_id`) REFERENCES `risk_acceptances` (`id`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_review_campaign_items_risk_id` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_entity_risk_reviewers_user_id` ON `entity_risk_reviewers` (`user_id`);

CREATE INDEX IF NOT EXISTS `IX_entity_risk_reviewers_appointed_by_id` ON `entity_risk_reviewers` (`appointed_by_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_entity_risk_reviewers_entity_user` ON `entity_risk_reviewers` (`entity_id`, `user_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_review_campaign_items_risk_id` ON `risk_review_campaign_items` (`risk_id`);

CREATE INDEX IF NOT EXISTS `IX_risk_review_campaign_items_decided_by_id` ON `risk_review_campaign_items` (`decided_by_id`);

CREATE INDEX IF NOT EXISTS `IX_risk_review_campaign_items_escalated_to_id` ON `risk_review_campaign_items` (`escalated_to_id`);

CREATE INDEX IF NOT EXISTS `IX_risk_review_campaign_items_risk_acceptance_id` ON `risk_review_campaign_items` (`risk_acceptance_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_review_campaign_items_campaign_risk` ON `risk_review_campaign_items` (`campaign_id`, `risk_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_review_campaigns_status_due_date` ON `risk_review_campaigns` (`status`, `due_date`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_review_campaigns_entity_period` ON `risk_review_campaigns` (`entity_id`, `period_start`, `period_end`);
