-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so this script carries no
-- transaction; every statement is guarded instead, and applying the version again converges on the
-- same schema.

-- M56, T265-T267 (S40 §6.8, §7.2, §7.3, §9.4) -- the Tenable Vulnerability Management connection, the per-source vendor
-- readings it collects (ACR and AES), and the heartbeat of a long sync run.
-- EF migration 20261118135340_TenableIntegration.

-- integration_sync_logs.heartbeat_at: stamped by every progress flush; the abandoned-run sweep measures from
-- coalesce(heartbeat_at, started_at), so a long export that still advances is never settled as dead.
ALTER TABLE `integration_sync_logs` ADD COLUMN IF NOT EXISTS `heartbeat_at` datetime NULL;

-- tenable_connections: one Tenable VM connection; both keys hold ciphertext or a vault reference and are never logged or
-- audited; next_due_at (beyond S40 §7.2) anchors the scheduled cadence; the default entity is SET NULL.
CREATE TABLE IF NOT EXISTS `tenable_connections` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `name` varchar(120) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `product` int(11) NOT NULL,
    `base_url` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `encrypted_access_key` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `encrypted_secret_key` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `entity_id` int(11) NULL,
    `enabled` tinyint(1) NOT NULL,
    `sync_interval_hours` int(11) NOT NULL,
    `sync_findings` tinyint(1) NOT NULL,
    `sync_assets` tinyint(1) NOT NULL,
    `collect_acr` tinyint(1) NOT NULL,
    `collect_aes` tinyint(1) NOT NULL,
    `min_severity` int(11) NOT NULL,
    `initial_window_days` int(11) NOT NULL,
    `include_unlicensed` tinyint(1) NOT NULL,
    `allow_invalid_certificate` tinyint(1) NOT NULL,
    `findings_cursor` datetime NULL,
    `assets_cursor` datetime NULL,
    `next_due_at` datetime NULL,
    `last_sync_at` datetime NULL,
    `last_sync_status` int(11) NULL,
    `last_sync_error` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_tenable_connections_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- vendor_scores: piecewise-constant vendor readings (S40 §7.2); T267 writes Tenable ACR and AES per host. vendor_asset_id
-- arrives with vendor_assets (T272); the host is CASCADE.
CREATE TABLE IF NOT EXISTS `vendor_scores` (
    `id` bigint(20) NOT NULL AUTO_INCREMENT,
    `source` int(11) NOT NULL,
    `connection_id` int(11) NOT NULL,
    `object_kind` int(11) NOT NULL,
    `host_id` int(11) NULL,
    `external_ref` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `entity_id` int(11) NULL,
    `value` double NOT NULL,
    `scale_max` double NOT NULL,
    `level` varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `details` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `first_captured_at` datetime NOT NULL,
    `last_captured_at` datetime NOT NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_vendor_scores_host_id` FOREIGN KEY (`host_id`) REFERENCES `hosts` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_tenable_connections_entity_id` ON `tenable_connections` (`entity_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_tenable_connections_name` ON `tenable_connections` (`name`);

CREATE INDEX IF NOT EXISTS `idx_vendor_scores_entity_id` ON `vendor_scores` (`entity_id`);

CREATE INDEX IF NOT EXISTS `idx_vendor_scores_host_id` ON `vendor_scores` (`host_id`);

CREATE INDEX IF NOT EXISTS `idx_vendor_scores_source_host` ON `vendor_scores` (`source`, `host_id`);

CREATE INDEX IF NOT EXISTS `idx_vendor_scores_source_ref` ON `vendor_scores` (`source`, `external_ref`);
