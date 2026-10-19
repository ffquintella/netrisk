-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so this script carries no
-- transaction; every statement is guarded instead, and applying the version again converges on the
-- same schema.

-- M53, T243 (S39 §12.5; S40 §7.1) -- the Consolidated Risk Index (ICR). EF migration
-- 20261016144648_RiskIndexTables. Three new tables, no ALTER on an existing one:
--   risk_index_profiles      the versioned, approved parameter sets (one in force at a time)
--   risk_index_snapshots     one row per pre-computed node, day and category (insert-only)
--   risk_index_object_days   one row per scored object, day and category (insert-only)

-- The history tables have no foreign key on entity_id or on the criticality author: they outlive an
-- entity's or a user's deletion (S40 §7.1). profile_id is RESTRICT: the profile is the evidence of what
-- a number meant. User references on the profile are SET NULL, as in every governance table, and no
-- CHECK names one of them (MariaDB refuses a CHECK on a column a SET NULL foreign key may clear).

-- The S40 §7.2 tables (attributions, node members, class days, bridges, preview requests, annotations,
-- entity risk-context notes) are not here: each arrives with the task that writes it (T246-T264).

CREATE TABLE IF NOT EXISTS `risk_index_profiles` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `version` int(11) NOT NULL,
    `name` varchar(120) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `preset` varchar(40) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `mode` int(11) NOT NULL,
    `status` int(11) NOT NULL,
    `parameters` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `justification` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `sensitivity_report` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `committee_decision_ref` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `board_approval_ref` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `is_rebaseline` tinyint(1) NOT NULL DEFAULT 0,
    `rejected_reason` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    `submitted_at` datetime NULL,
    `approved_at` datetime NULL,
    `approved_by_id` int(11) NULL,
    `approval_grant_source` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `effective_from` datetime NULL,
    `retired_at` datetime NULL,
    `retired_by_id` int(11) NULL,
    `retire_justification` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `retire_decision_ref` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `revision` int(11) NOT NULL DEFAULT 0,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_index_profiles_approved` CHECK (`status` NOT IN (3, 4) OR (`approved_at` IS NOT NULL AND `effective_from` IS NOT NULL AND `committee_decision_ref` IS NOT NULL)),
    CONSTRAINT `ck_risk_index_profiles_mode` CHECK (`mode` >= 1 AND `mode` <= 2),
    CONSTRAINT `ck_risk_index_profiles_rejected` CHECK (`status` <> 5 OR `rejected_reason` IS NOT NULL),
    CONSTRAINT `ck_risk_index_profiles_retired` CHECK ((`status` = 4 AND `retired_at` IS NOT NULL) OR (`status` <> 4 AND `retired_at` IS NULL)),
    CONSTRAINT `ck_risk_index_profiles_status` CHECK (`status` >= 1 AND `status` <= 5),
    CONSTRAINT `ck_risk_index_profiles_submitted` CHECK (`status` = 1 OR (`submitted_at` IS NOT NULL AND `justification` IS NOT NULL)),
    CONSTRAINT `ck_risk_index_profiles_version` CHECK (`version` >= 1),
    CONSTRAINT `fk_risk_index_profiles_approved_by_id` FOREIGN KEY (`approved_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_index_profiles_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_index_profiles_retired_by_id` FOREIGN KEY (`retired_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_index_snapshots` (
    `id` bigint(20) NOT NULL AUTO_INCREMENT,
    `snapshot_date` date NOT NULL,
    `profile_id` int(11) NOT NULL,
    `mode` int(11) NOT NULL,
    `scope_kind` int(11) NOT NULL,
    `scope_ref` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `entity_id` int(11) NULL,
    `category` int(11) NOT NULL,
    `value_unfloored` double NOT NULL,
    `value_floored` double NOT NULL,
    `value_displayed` int(11) NOT NULL,
    `floor_reasons` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `coverage` decimal(4,3) NOT NULL,
    `freshness` decimal(4,3) NOT NULL,
    `quality` decimal(4,3) NOT NULL,
    `interval_low` double NULL,
    `interval_high` double NULL,
    `object_count` int(11) NOT NULL,
    `presumption_points` double NOT NULL,
    `external_inputs_hash` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `top_contributions` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `change_attribution` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_index_snapshots_category` CHECK (`category` >= 0 AND `category` <= 6),
    CONSTRAINT `ck_risk_index_snapshots_fractions` CHECK (`coverage` >= 0 AND `coverage` <= 1 AND `freshness` >= 0 AND `freshness` <= 1 AND `quality` >= 0 AND `quality` <= 1),
    CONSTRAINT `ck_risk_index_snapshots_interval` CHECK ((`interval_low` IS NULL AND `interval_high` IS NULL) OR (`interval_low` IS NOT NULL AND `interval_high` IS NOT NULL AND `interval_low` <= `interval_high`)),
    CONSTRAINT `ck_risk_index_snapshots_mode` CHECK (`mode` >= 1 AND `mode` <= 2),
    CONSTRAINT `ck_risk_index_snapshots_scope_kind` CHECK (`scope_kind` >= 1 AND `scope_kind` <= 4),
    CONSTRAINT `ck_risk_index_snapshots_value_displayed` CHECK (`value_displayed` >= 0 AND `value_displayed` <= 100),
    CONSTRAINT `fk_risk_index_snapshots_profile_id` FOREIGN KEY (`profile_id`) REFERENCES `risk_index_profiles` (`id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_index_object_days` (
    `id` bigint(20) NOT NULL AUTO_INCREMENT,
    `snapshot_date` date NOT NULL,
    `profile_id` int(11) NOT NULL,
    `object_kind` int(11) NOT NULL,
    `object_ref` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `category` int(11) NOT NULL,
    `population` int(11) NOT NULL,
    `r_value` double NOT NULL,
    `weight` decimal(5,4) NOT NULL,
    `criticality` tinyint(4) NOT NULL,
    `criticality_source` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `criticality_author_id` int(11) NULL,
    `state` int(11) NOT NULL,
    `entity_id` int(11) NULL,
    `attribution` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `factors` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `scenario_inputs` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `signals` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `asset_class` int(11) NULL,
    `source_mask` int(11) NOT NULL DEFAULT 0,
    `environment` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_index_object_days_category` CHECK (`category` >= 1 AND `category` <= 6),
    CONSTRAINT `ck_risk_index_object_days_criticality` CHECK (`criticality` >= 1 AND `criticality` <= 5),
    CONSTRAINT `ck_risk_index_object_days_r_value` CHECK (`r_value` >= 0 AND `r_value` <= 1),
    CONSTRAINT `ck_risk_index_object_days_weight` CHECK (`weight` >= 0),
    CONSTRAINT `fk_risk_index_object_days_profile_id` FOREIGN KEY (`profile_id`) REFERENCES `risk_index_profiles` (`id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_risk_index_profiles_approved_by_id` ON `risk_index_profiles` (`approved_by_id`);
CREATE INDEX IF NOT EXISTS `idx_risk_index_profiles_created_by_id` ON `risk_index_profiles` (`created_by_id`);
CREATE INDEX IF NOT EXISTS `idx_risk_index_profiles_retired_by_id` ON `risk_index_profiles` (`retired_by_id`);
CREATE INDEX IF NOT EXISTS `idx_risk_index_profiles_status` ON `risk_index_profiles` (`status`);
CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_index_profiles_version` ON `risk_index_profiles` (`version`);

CREATE INDEX IF NOT EXISTS `idx_risk_index_snapshots_entity_id` ON `risk_index_snapshots` (`entity_id`);
CREATE INDEX IF NOT EXISTS `idx_risk_index_snapshots_profile_id` ON `risk_index_snapshots` (`profile_id`);
CREATE INDEX IF NOT EXISTS `idx_risk_index_snapshots_scope_category_date` ON `risk_index_snapshots` (`scope_kind`, `scope_ref`, `category`, `snapshot_date`);
CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_index_snapshots_date_profile_scope_category` ON `risk_index_snapshots` (`snapshot_date`, `profile_id`, `scope_kind`, `scope_ref`, `category`);

CREATE INDEX IF NOT EXISTS `idx_risk_index_object_days_date_class` ON `risk_index_object_days` (`snapshot_date`, `asset_class`);
CREATE INDEX IF NOT EXISTS `idx_risk_index_object_days_date_profile_category` ON `risk_index_object_days` (`snapshot_date`, `profile_id`, `category`);
CREATE INDEX IF NOT EXISTS `idx_risk_index_object_days_entity_id` ON `risk_index_object_days` (`entity_id`);
CREATE INDEX IF NOT EXISTS `idx_risk_index_object_days_profile_id` ON `risk_index_object_days` (`profile_id`);
CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_index_object_days_date_profile_object_category` ON `risk_index_object_days` (`snapshot_date`, `profile_id`, `object_kind`, `object_ref`, `category`);
