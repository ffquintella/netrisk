-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 9 Stage 9.9 (M47, T194-T199, S50) -- archival with reopening conditions, incident backtesting,
-- the risk committee and the third line.
--
--   risk_archives               The Phase 4 "archive" decision on a risk: its justification, the closure it
--                               made, the status to restore, the quarterly review date and, once, its reopening.
--   risk_archive_conditions     The Phase 7 triggers (Stage 9.8) whose event reopens the archive -- one per type.
--   risk_archive_reviews        Each quarterly review: kept (next review a quarter away) or reopened.
--   incident_backtests          An incident or near miss confronted with the register (one per incident).
--   incident_backtest_risks     The registered risks the assessor matched to it (one per backtest and risk).
--   risk_committees             The collegiate approver and its required approvals (two to fifty).
--   risk_committee_members      Its voting members (one per committee and user).
--   risk_committee_decisions    An acceptance or renewal submitted to it, and the acceptance its approval made.
--   risk_committee_votes        Each member's vote, final (one per decision and voter).
--
-- New tables only. Created in reference order: risk_archives (risks, closures, reassessment_events)
-- before its conditions and reviews; incident_backtests (incidents) before its risks; risk_committees
-- before its members and decisions (risk_acceptances), those before the votes. The third line adds no
-- table: its permission and role are rows (Data/98.sql).
--
-- Version 98 rather than the 98/99 S40 section 7.6 had reserved for M52 and M56/M57: this change
-- merged first, so S40 shifts its scripts to 99 and 100 (the rule #79, #80 and Stages 9.1-9.8 followed).

CREATE TABLE IF NOT EXISTS `risk_archives` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `risk_id` int(11) NOT NULL,
    `closure_id` int(11) NULL,
    `status` int(11) NOT NULL,
    `justification` varchar(4000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `previous_status` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `archived_at` datetime NOT NULL,
    `archived_by_id` int(11) NULL,
    `next_review_due_at` datetime NOT NULL,
    `last_reviewed_at` datetime NULL,
    `review_notified_at` datetime NULL,
    `reopened_at` datetime NULL,
    `reopen_origin` int(11) NULL,
    `reopened_by_id` int(11) NULL,
    `reopen_reason` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `reopen_event_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_archives_reopen_origin` CHECK (`reopen_origin` IS NULL OR (`reopen_origin` >= 1 AND `reopen_origin` <= 3)),
    CONSTRAINT `ck_risk_archives_reopened` CHECK ((`status` = 1 AND `reopened_at` IS NULL AND `reopen_origin` IS NULL) OR (`status` = 2 AND `reopened_at` IS NOT NULL AND `reopen_origin` IS NOT NULL AND `reopen_reason` IS NOT NULL)),
    CONSTRAINT `ck_risk_archives_status` CHECK (`status` >= 1 AND `status` <= 2),
    CONSTRAINT `fk_risk_archives_archived_by_id` FOREIGN KEY (`archived_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_archives_closure_id` FOREIGN KEY (`closure_id`) REFERENCES `closures` (`id`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_archives_reopen_event_id` FOREIGN KEY (`reopen_event_id`) REFERENCES `reassessment_events` (`id`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_archives_reopened_by_id` FOREIGN KEY (`reopened_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_archives_risk_id` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_archive_conditions` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `archive_id` int(11) NOT NULL,
    `trigger_type` int(11) NOT NULL,
    `description` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_archive_conditions_trigger_type` CHECK (`trigger_type` >= 1 AND `trigger_type` <= 6),
    CONSTRAINT `fk_risk_archive_conditions_archive_id` FOREIGN KEY (`archive_id`) REFERENCES `risk_archives` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_archive_reviews` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `archive_id` int(11) NOT NULL,
    `outcome` int(11) NOT NULL,
    `note` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `reviewed_at` datetime NOT NULL,
    `reviewed_by_id` int(11) NULL,
    `next_review_due_at` datetime NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_archive_reviews_next_due` CHECK ((`outcome` = 1 AND `next_review_due_at` IS NOT NULL) OR (`outcome` = 2 AND `next_review_due_at` IS NULL)),
    CONSTRAINT `ck_risk_archive_reviews_outcome` CHECK (`outcome` >= 1 AND `outcome` <= 2),
    CONSTRAINT `fk_risk_archive_reviews_archive_id` FOREIGN KEY (`archive_id`) REFERENCES `risk_archives` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_risk_archive_reviews_reviewed_by_id` FOREIGN KEY (`reviewed_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `incident_backtests` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `incident_id` int(11) NOT NULL,
    `note` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `assessed_at` datetime NOT NULL,
    `assessed_by_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_incident_backtests_assessed_by_id` FOREIGN KEY (`assessed_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_incident_backtests_incident_id` FOREIGN KEY (`incident_id`) REFERENCES `incidents` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `incident_backtest_risks` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `backtest_id` int(11) NOT NULL,
    `risk_id` int(11) NOT NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_incident_backtest_risks_backtest_id` FOREIGN KEY (`backtest_id`) REFERENCES `incident_backtests` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_incident_backtest_risks_risk_id` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_committees` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `name` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `mandate` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `entity_id` int(11) NULL,
    `required_approvals` int(11) NOT NULL,
    `retired_at` datetime NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    `updated_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_committees_required_approvals` CHECK (`required_approvals` >= 2 AND `required_approvals` <= 50),
    CONSTRAINT `fk_risk_committees_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_committees_updated_by_id` FOREIGN KEY (`updated_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_committee_members` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `committee_id` int(11) NOT NULL,
    `user_id` int(11) NOT NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_risk_committee_members_committee_id` FOREIGN KEY (`committee_id`) REFERENCES `risk_committees` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_risk_committee_members_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_committee_members_user_id` FOREIGN KEY (`user_id`) REFERENCES `user` (`value`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_committee_decisions` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `committee_id` int(11) NOT NULL,
    `risk_id` int(11) NOT NULL,
    `kind` int(11) NOT NULL,
    `renews_acceptance_id` int(11) NULL,
    `status` int(11) NOT NULL,
    `required_approvals` int(11) NOT NULL,
    `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `business_justification` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `compensating_controls` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `expires_at` datetime NOT NULL,
    `minutes_reference` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `opened_by_id` int(11) NULL,
    `opened_at` datetime NOT NULL,
    `closed_at` datetime NULL,
    `withdrawal_reason` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `acceptance_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    `version` int(11) NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_committee_decisions_closed` CHECK ((`status` = 1 AND `closed_at` IS NULL) OR (`status` <> 1 AND `closed_at` IS NOT NULL)),
    CONSTRAINT `ck_risk_committee_decisions_kind` CHECK (`kind` >= 1 AND `kind` <= 2),
    CONSTRAINT `ck_risk_committee_decisions_required_approvals` CHECK (`required_approvals` >= 2 AND `required_approvals` <= 50),
    CONSTRAINT `ck_risk_committee_decisions_status` CHECK (`status` >= 1 AND `status` <= 4),
    CONSTRAINT `ck_risk_committee_decisions_withdrawn` CHECK (`status` <> 4 OR `withdrawal_reason` IS NOT NULL),
    CONSTRAINT `fk_risk_committee_decisions_acceptance_id` FOREIGN KEY (`acceptance_id`) REFERENCES `risk_acceptances` (`id`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_committee_decisions_committee_id` FOREIGN KEY (`committee_id`) REFERENCES `risk_committees` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_risk_committee_decisions_opened_by_id` FOREIGN KEY (`opened_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_committee_decisions_renews_acceptance_id` FOREIGN KEY (`renews_acceptance_id`) REFERENCES `risk_acceptances` (`id`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_committee_decisions_risk_id` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_committee_votes` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `decision_id` int(11) NOT NULL,
    `voter_id` int(11) NULL,
    `choice` int(11) NOT NULL,
    `comment` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `cast_at` datetime NOT NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_committee_votes_choice` CHECK (`choice` >= 1 AND `choice` <= 3),
    CONSTRAINT `fk_risk_committee_votes_decision_id` FOREIGN KEY (`decision_id`) REFERENCES `risk_committee_decisions` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_risk_committee_votes_voter_id` FOREIGN KEY (`voter_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_incident_backtest_risks_risk_id` ON `incident_backtest_risks` (`risk_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_incident_backtest_risks_backtest_id_risk_id` ON `incident_backtest_risks` (`backtest_id`, `risk_id`);

CREATE INDEX IF NOT EXISTS `idx_incident_backtests_assessed_by_id` ON `incident_backtests` (`assessed_by_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_incident_backtests_incident_id` ON `incident_backtests` (`incident_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_archive_conditions_archive_id_trigger_type` ON `risk_archive_conditions` (`archive_id`, `trigger_type`);

CREATE INDEX IF NOT EXISTS `idx_risk_archive_reviews_archive_id` ON `risk_archive_reviews` (`archive_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_archive_reviews_reviewed_by_id` ON `risk_archive_reviews` (`reviewed_by_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_archives_archived_by_id` ON `risk_archives` (`archived_by_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_archives_closure_id` ON `risk_archives` (`closure_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_archives_reopen_event_id` ON `risk_archives` (`reopen_event_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_archives_reopened_by_id` ON `risk_archives` (`reopened_by_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_archives_risk_id` ON `risk_archives` (`risk_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_archives_status_next_review_due_at` ON `risk_archives` (`status`, `next_review_due_at`);

CREATE INDEX IF NOT EXISTS `idx_risk_committee_decisions_acceptance_id` ON `risk_committee_decisions` (`acceptance_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_committee_decisions_committee_id` ON `risk_committee_decisions` (`committee_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_committee_decisions_opened_by_id` ON `risk_committee_decisions` (`opened_by_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_committee_decisions_renews_acceptance_id` ON `risk_committee_decisions` (`renews_acceptance_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_committee_decisions_risk_id_status` ON `risk_committee_decisions` (`risk_id`, `status`);

CREATE INDEX IF NOT EXISTS `idx_risk_committee_members_created_by_id` ON `risk_committee_members` (`created_by_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_committee_members_user_id` ON `risk_committee_members` (`user_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_committee_members_committee_id_user_id` ON `risk_committee_members` (`committee_id`, `user_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_committee_votes_voter_id` ON `risk_committee_votes` (`voter_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_committee_votes_decision_id_voter_id` ON `risk_committee_votes` (`decision_id`, `voter_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_committees_entity_id` ON `risk_committees` (`entity_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_committees_updated_by_id` ON `risk_committees` (`updated_by_id`);
