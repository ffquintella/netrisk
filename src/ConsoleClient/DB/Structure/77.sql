-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 3 (ASPM) — vulnerability aggregation and finding lifecycle.
--
-- Additive throughout: nothing existing is dropped or rewritten, and the legacy
-- `vulnerabilities`.`Status` workflow column keeps its meaning. The new `status_id` is a separate,
-- closed triage lifecycle (Active / Verified / FalsePositive / OutOfScope / Duplicate /
-- RiskAccepted / Mitigated) with a transition matrix enforced in ServerServices; it is seeded from
-- `Status` below so an existing register starts out consistent rather than all-Active.
--
-- Every table here follows the Track 6 convention (snake_case, plural, `fk_`/`idx_`/`uq_` prefixes,
-- int-backed enums, tinyint(1) booleans, UTC datetimes) because new schema is expected to be born
-- compliant rather than added to the drift.

-- ---------------------------------------------------------------------------------------------
-- 3.2.3 — formal, expiring, authorized risk acceptance.
--
-- Created first: finding_status_history and files both reference it.
-- `expires_at` is NOT NULL deliberately. An acceptance with no expiry is the failure this table
-- exists to prevent — "accepted" quietly becoming "forgotten", with no date on which anyone is
-- obliged to look again.
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `risk_acceptances` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `business_justification` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `authorizing_manager_id` int(11) NOT NULL,
    `expires_at` datetime NOT NULL,
    `compensating_controls` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `residual_score_snapshot` double NULL,
    `status_id` int(11) NOT NULL DEFAULT 1,
    `entity_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    `updated_at` datetime NULL,
    `revoked_at` datetime NULL,
    `revoked_by_id` int(11) NULL,
    `revocation_reason` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `last_warning_days_before` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- The expiry job reads "active acceptances ordered by expiry"; the expiring-within-30-days filter
-- in the management view reads the same index.
CREATE INDEX IF NOT EXISTS `idx_ra_status_expires_at` ON `risk_acceptances` (`status_id`, `expires_at`);
CREATE INDEX IF NOT EXISTS `idx_ra_authorizing_manager_id` ON `risk_acceptances` (`authorizing_manager_id`);
CREATE INDEX IF NOT EXISTS `idx_ra_entity_id` ON `risk_acceptances` (`entity_id`);

-- RESTRICT on the authorizing manager: deleting the person who signed an acceptance must not
-- silently leave a live suppression nobody authorized.
ALTER TABLE `risk_acceptances`
    ADD CONSTRAINT `fk_ra_authorizing_manager_id` FOREIGN KEY IF NOT EXISTS (`authorizing_manager_id`)
        REFERENCES `user` (`value`) ON DELETE RESTRICT;
ALTER TABLE `risk_acceptances`
    ADD CONSTRAINT `fk_ra_created_by_id` FOREIGN KEY IF NOT EXISTS (`created_by_id`)
        REFERENCES `user` (`value`) ON DELETE SET NULL;
ALTER TABLE `risk_acceptances`
    ADD CONSTRAINT `fk_ra_revoked_by_id` FOREIGN KEY IF NOT EXISTS (`revoked_by_id`)
        REFERENCES `user` (`value`) ON DELETE SET NULL;
ALTER TABLE `risk_acceptances`
    ADD CONSTRAINT `fk_ra_entity_id` FOREIGN KEY IF NOT EXISTS (`entity_id`)
        REFERENCES `entities` (`Id`) ON DELETE SET NULL;

-- ---------------------------------------------------------------------------------------------
-- 3.1/3.3 — the scan-import log. Every import is reconstructible from this, and it is what
-- GET /vulnerabilities/import-jobs/{id} reads, so an import's outcome survives a restart instead
-- of living only in the in-memory job runner.
--
-- Created before vulnerabilities.last_import_id can reference it.
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `scan_imports` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `importer` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `file_name` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `file_id` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `user_id` int(11) NULL,
    `entity_id` int(11) NULL,
    `job_id` int(11) NULL,
    `started_at` datetime NOT NULL,
    `finished_at` datetime NULL,
    `status` int(11) NOT NULL,
    `new_count` int(11) NOT NULL DEFAULT 0,
    `updated_count` int(11) NOT NULL DEFAULT 0,
    `duplicate_count` int(11) NOT NULL DEFAULT 0,
    `closed_count` int(11) NOT NULL DEFAULT 0,
    `skipped_count` int(11) NOT NULL DEFAULT 0,
    `warning_count` int(11) NOT NULL DEFAULT 0,
    `new_by_severity` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `warnings` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `error_message` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `idempotency_key` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- The idempotency guarantee is enforced here rather than by a check-then-insert in the service:
-- two concurrent CI retries would both pass a service-side check. NULL is not compared by a MySQL
-- unique index, so imports without a key are unaffected.
CREATE UNIQUE INDEX IF NOT EXISTS `uq_scan_imports_idempotency_key` ON `scan_imports` (`idempotency_key`);
CREATE INDEX IF NOT EXISTS `idx_scan_imports_importer_started_at` ON `scan_imports` (`importer`, `started_at`);
CREATE INDEX IF NOT EXISTS `idx_scan_imports_job_id` ON `scan_imports` (`job_id`);
CREATE INDEX IF NOT EXISTS `idx_scan_imports_entity_id` ON `scan_imports` (`entity_id`);

ALTER TABLE `scan_imports`
    ADD CONSTRAINT `fk_scan_imports_user_id` FOREIGN KEY IF NOT EXISTS (`user_id`)
        REFERENCES `user` (`value`) ON DELETE SET NULL;
ALTER TABLE `scan_imports`
    ADD CONSTRAINT `fk_scan_imports_entity_id` FOREIGN KEY IF NOT EXISTS (`entity_id`)
        REFERENCES `entities` (`Id`) ON DELETE SET NULL;

-- ---------------------------------------------------------------------------------------------
-- 3.1/3.2/3.3/3.4 — the ASPM columns on the finding itself.
-- ---------------------------------------------------------------------------------------------

-- 3.2.1 — the triage lifecycle. Default 1 (Active).
ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `status_id` int(11) NOT NULL DEFAULT 1;

-- 3.3.1 — the dedup key, computed once at import and never recomputed. 64 chars: a SHA-256 digest
-- in hex, or a tool-native id hashed to the same width so every key is one comparable shape.
ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `dedup_key` varchar(64)
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;
ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `dedup_strategy` varchar(32)
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

-- 3.4.2 — first_detection plus the remediation allowance for this severity. DaysOverdue is derived
-- at query time and deliberately not stored, so it cannot drift.
ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `sla_due_date` datetime NULL;

-- 3.1.1 — the normalized scanner fields the importers produce. Without rule_id and location a
-- code-scanner finding has no stable identity to deduplicate on at all.
ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `rule_id` varchar(255)
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;
ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `tool_unique_id` varchar(255)
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;
ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `location` text
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;
ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `component` varchar(255)
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;
ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `component_version` varchar(255)
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;
ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `fixed_in_version` varchar(255)
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;
ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `raw_severity` varchar(64)
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;
ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `cwes` text
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;
ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `last_import_id` int(11) NULL;
ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `duplicate_of_id` int(11) NULL;

-- The dedup lookup is one indexed equality read per imported finding — the difference between an
-- import that scales to a 100k-finding scan and one that does not.
CREATE INDEX IF NOT EXISTS `idx_vulnerabilities_dedup_key` ON `vulnerabilities` (`dedup_key`);
CREATE INDEX IF NOT EXISTS `idx_vulnerabilities_status_id` ON `vulnerabilities` (`status_id`);
CREATE INDEX IF NOT EXISTS `idx_vulnerabilities_sla_due_date` ON `vulnerabilities` (`sla_due_date`);
CREATE INDEX IF NOT EXISTS `idx_vulnerabilities_duplicate_of_id` ON `vulnerabilities` (`duplicate_of_id`);

-- SET NULL rather than CASCADE: deleting the canonical finding must not delete the duplicates that
-- point at it — they become ordinary findings again, which is recoverable, whereas cascading would
-- silently destroy real data.
ALTER TABLE `vulnerabilities`
    ADD CONSTRAINT `fk_vulnerabilities_duplicate_of_id` FOREIGN KEY IF NOT EXISTS (`duplicate_of_id`)
        REFERENCES `vulnerabilities` (`Id`) ON DELETE SET NULL;

-- ---------------------------------------------------------------------------------------------
-- 3.2.1 — seed the lifecycle from the legacy workflow column.
--
-- Findings the register already treats as resolved must not come back as Active, and findings a
-- triager already rejected must not lose that verdict — a re-import would otherwise resurrect
-- every false positive ever dismissed. Values not listed keep the default (Active), which is the
-- safe direction: an over-open register is a nuisance, an under-open one hides real work.
--
-- Legacy IntStatus values referenced: 4 Closed, 6 NotRelevant, 8 Mitigated, 11 Rejected,
-- 12 Duplicated, 22 FixNotRequired, 25 Fixed, 26 Solved, 31 FixVerified, 41 Verified.
-- ---------------------------------------------------------------------------------------------
UPDATE `vulnerabilities` SET `status_id` = 7 WHERE `Status` IN (4, 8, 25, 26, 31);
UPDATE `vulnerabilities` SET `status_id` = 3 WHERE `Status` = 11;
UPDATE `vulnerabilities` SET `status_id` = 4 WHERE `Status` IN (6, 22);
UPDATE `vulnerabilities` SET `status_id` = 5 WHERE `Status` = 12;
UPDATE `vulnerabilities` SET `status_id` = 2 WHERE `Status` = 41;

-- ---------------------------------------------------------------------------------------------
-- 3.2.2 — the append-only audit trail of state transitions.
--
-- There is no update or delete path to this table anywhere in the API. That is the whole value of
-- it: nobody can quietly rewrite the record of who suppressed what, which is what makes it an
-- auditor-facing artifact rather than a debug log.
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `finding_status_history` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `vulnerability_id` int(11) NOT NULL,
    -- NULL for the row that records a finding's creation: there is no state it came from, and
    -- writing Active there would misrepresent a new finding as a transition.
    `from_status_id` int(11) NULL,
    `to_status_id` int(11) NOT NULL,
    `user_id` int(11) NULL,
    `changed_at` datetime NOT NULL,
    `source` int(11) NOT NULL,
    `justification` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `risk_acceptance_id` int(11) NULL,
    `duplicate_of_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- The timeline is always read as "this finding, newest first".
CREATE INDEX IF NOT EXISTS `idx_fsh_vulnerability_changed_at` ON `finding_status_history` (`vulnerability_id`, `changed_at`);
CREATE INDEX IF NOT EXISTS `idx_fsh_user_id` ON `finding_status_history` (`user_id`);
CREATE INDEX IF NOT EXISTS `idx_fsh_risk_acceptance_id` ON `finding_status_history` (`risk_acceptance_id`);

-- History follows its finding into deletion: an orphan timeline for a finding nobody can look up
-- is not evidence of anything. The actor, by contrast, is nulled rather than cascaded — "someone,
-- on this date, with this justification" is still a better record than no row at all.
ALTER TABLE `finding_status_history`
    ADD CONSTRAINT `fk_fsh_vulnerability_id` FOREIGN KEY IF NOT EXISTS (`vulnerability_id`)
        REFERENCES `vulnerabilities` (`Id`) ON DELETE CASCADE;
ALTER TABLE `finding_status_history`
    ADD CONSTRAINT `fk_fsh_user_id` FOREIGN KEY IF NOT EXISTS (`user_id`)
        REFERENCES `user` (`value`) ON DELETE SET NULL;
ALTER TABLE `finding_status_history`
    ADD CONSTRAINT `fk_fsh_risk_acceptance_id` FOREIGN KEY IF NOT EXISTS (`risk_acceptance_id`)
        REFERENCES `risk_acceptances` (`id`) ON DELETE SET NULL;

-- ---------------------------------------------------------------------------------------------
-- 3.2.3 — which findings an acceptance covers. An explicit join table rather than an implicit
-- many-to-many so the row can record when the finding came under the acceptance: findings get
-- added to a live acceptance as later scans surface them, and the timeline needs that date.
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `risk_acceptance_findings` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `risk_acceptance_id` int(11) NOT NULL,
    `vulnerability_id` int(11) NOT NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- The same finding twice under one acceptance would double-count it on expiry.
CREATE UNIQUE INDEX IF NOT EXISTS `uq_raf_acceptance_finding`
    ON `risk_acceptance_findings` (`risk_acceptance_id`, `vulnerability_id`);
CREATE INDEX IF NOT EXISTS `idx_raf_vulnerability_id` ON `risk_acceptance_findings` (`vulnerability_id`);

ALTER TABLE `risk_acceptance_findings`
    ADD CONSTRAINT `fk_raf_risk_acceptance_id` FOREIGN KEY IF NOT EXISTS (`risk_acceptance_id`)
        REFERENCES `risk_acceptances` (`id`) ON DELETE CASCADE;
ALTER TABLE `risk_acceptance_findings`
    ADD CONSTRAINT `fk_raf_vulnerability_id` FOREIGN KEY IF NOT EXISTS (`vulnerability_id`)
        REFERENCES `vulnerabilities` (`Id`) ON DELETE CASCADE;

-- 3.2.3 — evidence attached to an acceptance (the approval email, the signed exception form).
-- Follows the existing one-nullable-FK-per-attachment-target pattern on `nr_files`.
ALTER TABLE `nr_files` ADD COLUMN IF NOT EXISTS `risk_acceptance_id` int(11) NULL;
CREATE INDEX IF NOT EXISTS `idx_files_risk_acceptance_id` ON `nr_files` (`risk_acceptance_id`);
ALTER TABLE `nr_files`
    ADD CONSTRAINT `fk_files_risk_acceptance_id` FOREIGN KEY IF NOT EXISTS (`risk_acceptance_id`)
        REFERENCES `risk_acceptances` (`id`) ON DELETE CASCADE;

-- ---------------------------------------------------------------------------------------------
-- 3.3.3 — per-scanner deduplication configuration, plus its change history.
--
-- The history matters because a dedup heuristic change silently alters what counts as "the same
-- finding" from that point on; when the register's numbers jump, this is the table that explains it.
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `scanner_dedup_configurations` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `importer` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `strategy_chain` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `hash_fields` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    -- Default off. A partial scan mistaken for a full one closes everything outside its slice,
    -- which is far worse than a stale open finding.
    `auto_close_missing` tinyint(1) NOT NULL DEFAULT 0,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    `updated_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- One configuration per importer; a second row would make "which one applies" a coin toss.
CREATE UNIQUE INDEX IF NOT EXISTS `uq_sdc_importer` ON `scanner_dedup_configurations` (`importer`);

ALTER TABLE `scanner_dedup_configurations`
    ADD CONSTRAINT `fk_sdc_updated_by_id` FOREIGN KEY IF NOT EXISTS (`updated_by_id`)
        REFERENCES `user` (`value`) ON DELETE SET NULL;

CREATE TABLE IF NOT EXISTS `scanner_dedup_configuration_history` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `importer` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `old_strategy_chain` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `new_strategy_chain` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `old_hash_fields` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `new_hash_fields` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `old_auto_close_missing` tinyint(1) NULL,
    `new_auto_close_missing` tinyint(1) NOT NULL,
    `user_id` int(11) NULL,
    `changed_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_sdch_importer_changed_at`
    ON `scanner_dedup_configuration_history` (`importer`, `changed_at`);

ALTER TABLE `scanner_dedup_configuration_history`
    ADD CONSTRAINT `fk_sdch_user_id` FOREIGN KEY IF NOT EXISTS (`user_id`)
        REFERENCES `user` (`value`) ON DELETE SET NULL;

-- ---------------------------------------------------------------------------------------------
-- 3.4.1 — SLA policy, effective-dated.
--
-- Changing a policy inserts a new row and closes the old one rather than editing in place, so a
-- finding's due date stays derivable from the policy in force when it was found. Editing in place
-- would silently rewrite last quarter's compliance figures, which is the one thing an SLA report
-- must never do.
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `sla_configurations` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `severity` int(11) NOT NULL,
    `max_triage_days` int(11) NOT NULL,
    `max_remediation_days` int(11) NOT NULL,
    -- NULL for the global default; set for an entity-specific override.
    `entity_id` int(11) NULL,
    `effective_from` datetime NOT NULL,
    `effective_to` datetime NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_slac_severity_entity_from`
    ON `sla_configurations` (`severity`, `entity_id`, `effective_from`);

ALTER TABLE `sla_configurations`
    ADD CONSTRAINT `fk_slac_entity_id` FOREIGN KEY IF NOT EXISTS (`entity_id`)
        REFERENCES `entities` (`Id`) ON DELETE CASCADE;
ALTER TABLE `sla_configurations`
    ADD CONSTRAINT `fk_slac_created_by_id` FOREIGN KEY IF NOT EXISTS (`created_by_id`)
        REFERENCES `user` (`value`) ON DELETE SET NULL;

-- ---------------------------------------------------------------------------------------------
-- 3.4.3 — the notification idempotence guard. One row per (finding, threshold, due date).
--
-- The due date is part of the key so that legitimately moving a deadline (a severity change) re-arms
-- the warning, while re-running the job on an unchanged deadline sends nothing.
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `sla_notifications` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `vulnerability_id` int(11) NOT NULL,
    `threshold_days` int(11) NOT NULL,
    `notified_at` datetime NOT NULL,
    `due_date` datetime NOT NULL,
    `recipient_user_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_slan_vulnerability_threshold_due`
    ON `sla_notifications` (`vulnerability_id`, `threshold_days`, `due_date`);

ALTER TABLE `sla_notifications`
    ADD CONSTRAINT `fk_slan_vulnerability_id` FOREIGN KEY IF NOT EXISTS (`vulnerability_id`)
        REFERENCES `vulnerabilities` (`Id`) ON DELETE CASCADE;
ALTER TABLE `sla_notifications`
    ADD CONSTRAINT `fk_slan_recipient_user_id` FOREIGN KEY IF NOT EXISTS (`recipient_user_id`)
        REFERENCES `user` (`value`) ON DELETE SET NULL;

-- ---------------------------------------------------------------------------------------------
-- 3.5.1 — scoped, revocable API tokens for CI runners.
--
-- The secret is never stored, only its hash: a leaked database dump therefore does not hand over
-- working tokens, and there is no code path that can display a token again after issue.
-- `key_id` is the public half, kept in clear so a presented token is one indexed read rather than a
-- hash comparison against every row.
-- ---------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `api_tokens` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `key_id` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `secret_hash` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `scopes` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `expires_at` datetime NULL,
    `entity_id` int(11) NULL,
    `user_id` int(11) NOT NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    `last_used_at` datetime NULL,
    `revoked_at` datetime NULL,
    `revoked_by_id` int(11) NULL,
    `rate_limit_per_minute` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_api_tokens_key_id` ON `api_tokens` (`key_id`);
CREATE INDEX IF NOT EXISTS `idx_api_tokens_user_id` ON `api_tokens` (`user_id`);
CREATE INDEX IF NOT EXISTS `idx_api_tokens_entity_id` ON `api_tokens` (`entity_id`);

-- CASCADE on the owning user: a credential that outlives the identity it acts as is a credential
-- nobody owns.
ALTER TABLE `api_tokens`
    ADD CONSTRAINT `fk_api_tokens_user_id` FOREIGN KEY IF NOT EXISTS (`user_id`)
        REFERENCES `user` (`value`) ON DELETE CASCADE;
ALTER TABLE `api_tokens`
    ADD CONSTRAINT `fk_api_tokens_created_by_id` FOREIGN KEY IF NOT EXISTS (`created_by_id`)
        REFERENCES `user` (`value`) ON DELETE SET NULL;
ALTER TABLE `api_tokens`
    ADD CONSTRAINT `fk_api_tokens_revoked_by_id` FOREIGN KEY IF NOT EXISTS (`revoked_by_id`)
        REFERENCES `user` (`value`) ON DELETE SET NULL;
ALTER TABLE `api_tokens`
    ADD CONSTRAINT `fk_api_tokens_entity_id` FOREIGN KEY IF NOT EXISTS (`entity_id`)
        REFERENCES `entities` (`Id`) ON DELETE SET NULL;

