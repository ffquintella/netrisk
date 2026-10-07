-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 9 Stage 9.4 (M42, T162-T167, S45) -- exploitation signals: CISA KEV, first-class EPSS and
-- MITRE ATT&CK.
--
--   vulnerabilities.epss_*            The effective EPSS reading of a finding (score, percentile, source,
--                                     the CVE it came from, its reference date, when NetRisk last changed
--                                     it). NULL on every existing row: the first EPSS sync fills them.
--                                     epss_score is the double column S40 section 7.3 reserved.
--   epss_scores                       One reading per CVE and source (FIRST, Vision One), both kept.
--   kev_entries                       The CISA KEV catalogue. Rows are never deleted: delisted_at and
--                                     delisting_held_since record leaving it.
--   exploitation_signal_syncs         One row per sync run, failures included.
--   vulnerability_attack_techniques   ATT&CK techniques of a finding, CASCADE with the finding.
--   risk_attack_techniques            ATT&CK techniques of a risk scenario, CASCADE with the risk.
--
-- Version 93 rather than the 93/94 S40 section 7.6 had reserved for M52 and M56/M57: this change
-- merged first, so S40 shifts its scripts to 94 and 95 (the rule #79, #80 and Stages 9.1-9.3 followed).

ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `epss_as_of` datetime NULL;

ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `epss_cve` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `epss_percentile` double NULL;

ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `epss_score` double NULL;

ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `epss_source` int(11) NULL;

ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `epss_updated_at` datetime NULL;

CREATE INDEX IF NOT EXISTS `idx_vulnerabilities_epss_score` ON `vulnerabilities` (`epss_score`);

CREATE TABLE IF NOT EXISTS `epss_scores` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `cve_id` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `source` int(11) NOT NULL,
    `score` double NOT NULL,
    `percentile` double NULL,
    `as_of` datetime NOT NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_epss_scores_range` CHECK (`score` >= 0 AND `score` <= 1 AND (`percentile` IS NULL OR (`percentile` >= 0 AND `percentile` <= 1)))
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_epss_scores_cve_id_source` ON `epss_scores` (`cve_id`, `source`);

CREATE TABLE IF NOT EXISTS `kev_entries` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `cve_id` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `vendor_project` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `product` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `vulnerability_name` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `short_description` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `required_action` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `notes` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `cwes` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `date_added` datetime NOT NULL,
    `due_date` datetime NULL,
    `known_ransomware_use` tinyint(1) NOT NULL DEFAULT FALSE,
    `delisted_at` datetime NULL,
    `delisting_held_since` datetime NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_kev_entries_cve_id` ON `kev_entries` (`cve_id`);

CREATE TABLE IF NOT EXISTS `exploitation_signal_syncs` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `feed` int(11) NOT NULL,
    `outcome` int(11) NOT NULL,
    `started_at` datetime NOT NULL,
    `finished_at` datetime NOT NULL,
    `catalogue_version` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `catalogue_released_at` datetime NULL,
    `received_count` int(11) NOT NULL,
    `added_count` int(11) NOT NULL,
    `updated_count` int(11) NOT NULL,
    `unchanged_count` int(11) NOT NULL,
    `delisted_count` int(11) NOT NULL,
    `relisted_count` int(11) NOT NULL,
    `held_count` int(11) NOT NULL,
    `findings_updated_count` int(11) NOT NULL,
    `error` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_exploitation_signal_syncs_feed_started_at` ON `exploitation_signal_syncs` (`feed`, `started_at`);

CREATE TABLE IF NOT EXISTS `vulnerability_attack_techniques` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `vulnerability_id` int(11) NOT NULL,
    `technique_id` varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `technique_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_vulnerability_attack_techniques_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_vulnerability_attack_techniques_vulnerability_id` FOREIGN KEY (`vulnerability_id`) REFERENCES `vulnerabilities` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 64 characters: exactly MariaDB's identifier limit.
CREATE UNIQUE INDEX IF NOT EXISTS `uq_vulnerability_attack_techniques_vulnerability_id_technique_id` ON `vulnerability_attack_techniques` (`vulnerability_id`, `technique_id`);
CREATE INDEX IF NOT EXISTS `idx_vulnerability_attack_techniques_created_by_id` ON `vulnerability_attack_techniques` (`created_by_id`);

CREATE TABLE IF NOT EXISTS `risk_attack_techniques` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `risk_id` int(11) NOT NULL,
    `technique_id` varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `technique_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_risk_attack_techniques_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_attack_techniques_risk_id` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_attack_techniques_risk_id_technique_id` ON `risk_attack_techniques` (`risk_id`, `technique_id`);
CREATE INDEX IF NOT EXISTS `idx_risk_attack_techniques_created_by_id` ON `risk_attack_techniques` (`created_by_id`);
