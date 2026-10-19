-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so this script carries no
-- transaction; every statement is guarded instead, and applying the version again converges on the
-- same schema.

-- M53, T250 (S39 §9.5, §13.2; S40 §6.3, §6.4, §7.2, §9.3 step 4, §9.6) -- the profile lifecycle's two tables.
-- EF migration 20261016172322_RiskIndexBridgesAndPreviews.

-- New tables:
--   risk_index_bridges           on the first day of a newly active profile, each node and category under both
--                                versions (the A_cfg of that day's "what moved") and the 90-day overlay; insert-only,
--                                written by the snapshot job; scope filter on entity_id; no entity foreign key
--   risk_index_preview_requests  a draft's sensitivity previews, queued by the API and computed by RiskIndexPreviewJob;
--                                the report lives here until the submission copies it to the profile; deleting the
--                                draft deletes its previews (CASCADE)

CREATE TABLE IF NOT EXISTS `risk_index_bridges` (
    `id` bigint(20) NOT NULL AUTO_INCREMENT,
    `from_profile_id` int(11) NOT NULL,
    `to_profile_id` int(11) NOT NULL,
    `bridge_date` date NOT NULL,
    `scope_kind` int(11) NOT NULL,
    `scope_ref` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `entity_id` int(11) NULL,
    `category` int(11) NOT NULL,
    `value_old` double NOT NULL,
    `value_new` double NOT NULL,
    `overlay` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `overlay_partial` tinyint(1) NOT NULL DEFAULT FALSE,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_index_bridges_category` CHECK (`category` >= 0 AND `category` <= 6),
    CONSTRAINT `ck_risk_index_bridges_profiles` CHECK (`from_profile_id` <> `to_profile_id`),
    CONSTRAINT `ck_risk_index_bridges_scope_kind` CHECK (`scope_kind` >= 1 AND `scope_kind` <= 4),
    CONSTRAINT `fk_risk_index_bridges_from_profile_id` FOREIGN KEY (`from_profile_id`) REFERENCES `risk_index_profiles` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_risk_index_bridges_to_profile_id` FOREIGN KEY (`to_profile_id`) REFERENCES `risk_index_profiles` (`id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_index_preview_requests` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `profile_id` int(11) NOT NULL,
    `parameters_hash` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `status` int(11) NOT NULL,
    `progress` int(11) NOT NULL DEFAULT 0,
    `requested_by_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    `started_at` datetime NULL,
    `heartbeat_at` datetime NULL,
    `finished_at` datetime NULL,
    `error_message` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `report` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_index_preview_requests_progress` CHECK (`progress` >= 0 AND `progress` <= 100),
    CONSTRAINT `ck_risk_index_preview_requests_status` CHECK (`status` >= 1 AND `status` <= 4),
    CONSTRAINT `fk_risk_index_preview_requests_profile_id` FOREIGN KEY (`profile_id`) REFERENCES `risk_index_profiles` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_risk_index_preview_requests_requested_by_id` FOREIGN KEY (`requested_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_risk_index_bridges_entity_id` ON `risk_index_bridges` (`entity_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_index_bridges_from_profile_id` ON `risk_index_bridges` (`from_profile_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_index_bridges_to_scope_category` ON `risk_index_bridges` (`to_profile_id`, `scope_kind`, `scope_ref`, `category`);

CREATE INDEX IF NOT EXISTS `idx_risk_index_preview_requests_profile_id` ON `risk_index_preview_requests` (`profile_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_index_preview_requests_requested_by_id` ON `risk_index_preview_requests` (`requested_by_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_index_preview_requests_status` ON `risk_index_preview_requests` (`status`);
