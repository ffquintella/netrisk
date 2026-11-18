-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so this script carries no
-- transaction; every statement is guarded instead, and applying the version again converges on the
-- same schema.

-- M57, T270-T276 (S40 §6.9, §7.2, §7.3, §9.5; S39 D-09 to D-13) -- the Vision One and SecurityScorecard posture expansion.
-- EF migration 20261118160000_VendorPostureExpansion.

-- trendmicro_connections: the three new sync phases (off by default, like every option that widens what a sync collects)
-- and the anchored due date of the scheduled sync (T276; beyond S40 §7.3, which listed it only for Tenable).
ALTER TABLE `trendmicro_connections` ADD COLUMN IF NOT EXISTS `sync_posture` tinyint(1) NOT NULL DEFAULT 0;
ALTER TABLE `trendmicro_connections` ADD COLUMN IF NOT EXISTS `sync_attack_surface_objects` tinyint(1) NOT NULL DEFAULT 0;
ALTER TABLE `trendmicro_connections` ADD COLUMN IF NOT EXISTS `sync_workbench_alerts` tinyint(1) NOT NULL DEFAULT 0;
ALTER TABLE `trendmicro_connections` ADD COLUMN IF NOT EXISTS `next_due_at` datetime NULL;

-- securityscorecard_connections: the score-history backfill (T274) and the anchored due date (T276).
ALTER TABLE `securityscorecard_connections` ADD COLUMN IF NOT EXISTS `history_backfill_days` int(11) NOT NULL DEFAULT 365;
ALTER TABLE `securityscorecard_connections` ADD COLUMN IF NOT EXISTS `history_backfilled_from` date NULL;
ALTER TABLE `securityscorecard_connections` ADD COLUMN IF NOT EXISTS `store_issue_impact` tinyint(1) NOT NULL DEFAULT 1;
ALTER TABLE `securityscorecard_connections` ADD COLUMN IF NOT EXISTS `next_due_at` datetime NULL;

-- security_scorecard_factors: a factor without a score is NULL, never 0 (D-12). MODIFY restates the target definition, so
-- applying it again converges. is_backfill marks a row recovered from the history endpoints.
ALTER TABLE `security_scorecard_factors` MODIFY COLUMN `score` int(11) NULL;
ALTER TABLE `security_scorecard_factors` ADD COLUMN IF NOT EXISTS `is_backfill` tinyint(1) NOT NULL DEFAULT 0;

-- hosts.criticality_source: who wrote hosts.criticality (manual, cmdb, visionone; NULL = unknown), for the precedence of
-- S39 §5.1 (D-10).
ALTER TABLE `hosts` ADD COLUMN IF NOT EXISTS `criticality_source` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

-- vulnerabilities: Vision One's exploit attempts and global exploit activity, kept apart (D-11). epss_score exists since 93.
ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `exploit_attempt_count` int(11) NULL;
ALTER TABLE `vulnerabilities` ADD COLUMN IF NOT EXISTS `global_exploit_activity` varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

-- vendor_assets: Vision One internet-facing FQDNs/IPs, accounts, cloud assets and local apps (T272); the entity is SET NULL.
CREATE TABLE IF NOT EXISTS `vendor_assets` (
    `id` bigint(20) NOT NULL AUTO_INCREMENT,
    `provider` int(11) NOT NULL,
    `connection_id` int(11) NOT NULL,
    `asset_kind` int(11) NOT NULL,
    `external_id` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `criticality` int(11) NULL,
    `criticality_raw` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `entity_id` int(11) NULL,
    `tags` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `first_seen_at` datetime NOT NULL,
    `last_seen_at` datetime NOT NULL,
    `status` int(11) NOT NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_vendor_assets_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- security_scorecard_issue_summaries: the score impact per issue type of a factor capture (T274); CASCADE with the factor.
CREATE TABLE IF NOT EXISTS `security_scorecard_issue_summaries` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `factor_id` int(11) NOT NULL,
    `issue_type` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `severity` varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `issue_count` int(11) NULL,
    `total_score_impact` double NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_security_scorecard_issue_summaries_factor_id` FOREIGN KEY (`factor_id`) REFERENCES `security_scorecard_factors` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- vendor_scores.vendor_asset_id: the reading of a vendor asset (T272; M56 left it out); CASCADE with the asset.
ALTER TABLE `vendor_scores` ADD COLUMN IF NOT EXISTS `vendor_asset_id` bigint(20) NULL;

CREATE INDEX IF NOT EXISTS `idx_vendor_assets_entity_id` ON `vendor_assets` (`entity_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_vendor_assets_provider_connection_kind_external_id` ON `vendor_assets` (`provider`, `connection_id`, `asset_kind`, `external_id`);

CREATE INDEX IF NOT EXISTS `idx_security_scorecard_issue_summaries_factor_id` ON `security_scorecard_issue_summaries` (`factor_id`);

CREATE INDEX IF NOT EXISTS `idx_vendor_scores_source_asset` ON `vendor_scores` (`source`, `vendor_asset_id`);

CREATE INDEX IF NOT EXISTS `idx_vendor_scores_vendor_asset_id` ON `vendor_scores` (`vendor_asset_id`);

ALTER TABLE `vendor_scores` ADD CONSTRAINT `fk_vendor_scores_vendor_asset_id` FOREIGN KEY IF NOT EXISTS (`vendor_asset_id`) REFERENCES `vendor_assets` (`id`) ON DELETE CASCADE;
