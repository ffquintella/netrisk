-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 9 Stage 9.5 (M43, T168-T173, S46) -- the eleven mandatory flags and Gate A.
--
--   risk_flags       One row per risk and flag (1-11, and 12 for the Gate A condition "no legitimate
--                    acceptance"): the declared half with its reason, the derived half with its basis
--                    (flag 3 from KEV, flag 4 from the BIA, flag 5 from data classification). Created on
--                    the first declaration or derivation, never deleted except with the risk.
--   risk_decisions   The insert-only Phase 4 decision log: act immediately, treat in cycle, monitor/accept,
--                    archive; declared by a person or recorded by the system at a Gate A onset.
--
-- Version 94 rather than the 94/95 S40 section 7.6 had reserved for M52 and M56/M57: this change
-- merged first, so S40 shifts its scripts to 95 and 96 (the rule #79, #80 and Stages 9.1-9.4 followed).

CREATE TABLE IF NOT EXISTS `risk_flags` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `risk_id` int(11) NOT NULL,
    `flag` int(11) NOT NULL,
    `declared` tinyint(1) NOT NULL DEFAULT FALSE,
    `declared_reason` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `declared_at` datetime NULL,
    `declared_by_id` int(11) NULL,
    `derived` tinyint(1) NOT NULL DEFAULT FALSE,
    `derived_basis` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `derived_weight` decimal(4,2) NULL,
    `derived_changed_at` datetime NULL,
    `derived_note` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_flags_flag` CHECK (`flag` >= 1 AND `flag` <= 12),
    CONSTRAINT `fk_risk_flags_declared_by_id` FOREIGN KEY (`declared_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_flags_risk_id` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_flags_risk_id_flag` ON `risk_flags` (`risk_id`, `flag`);
CREATE INDEX IF NOT EXISTS `idx_risk_flags_flag` ON `risk_flags` (`flag`);
CREATE INDEX IF NOT EXISTS `idx_risk_flags_declared_by_id` ON `risk_flags` (`declared_by_id`);

CREATE TABLE IF NOT EXISTS `risk_decisions` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `risk_id` int(11) NOT NULL,
    `decision` int(11) NOT NULL,
    `source` int(11) NOT NULL,
    `reason` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `gate_a_conditions` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `decided_at` datetime NOT NULL,
    `decided_by_id` int(11) NULL,
    `escalated_at` datetime NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_decisions_decision` CHECK (`decision` >= 1 AND `decision` <= 4),
    CONSTRAINT `ck_risk_decisions_source` CHECK (`source` >= 1 AND `source` <= 2),
    CONSTRAINT `fk_risk_decisions_decided_by_id` FOREIGN KEY (`decided_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_decisions_risk_id` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_risk_decisions_risk_id_decided_at` ON `risk_decisions` (`risk_id`, `decided_at`);
CREATE INDEX IF NOT EXISTS `idx_risk_decisions_decided_by_id` ON `risk_decisions` (`decided_by_id`);
