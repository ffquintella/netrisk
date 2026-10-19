-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so this script carries no
-- transaction; every statement is guarded instead, and applying the version again converges on the
-- same schema.

-- M53, T247 (S39 §9.1, §9.5; S40 §7.2, §9.3) -- what the daily RiskIndexSnapshotJob writes beside the
-- snapshots and object rows of version 106. EF migration 20261016162414_RiskIndexSnapshotJobTables.

-- New tables:
--   risk_index_object_attributions  one row per object and entity of attribution (null = Unassigned)
--   risk_index_node_members         the closure and scope entities an official read compares claims with
--   risk_index_class_days           the C_k of each pre-computed node intersected with an asset-class tab
--   risk_index_incident_closures    the closure date the first snapshot froze (S39 §9.2, D-15 interim)

-- New columns on the two tables of 106:
--   risk_index_snapshots    display_state / category_state (S39 §8.1, §8.6), markers (§9.5), chips (§9.1)
--   risk_index_object_days  freshness, coverage_effect, r_max, cleared_r: the engine's inputs beside R and
--                           the weight, so D-1, the 90-day replay and a derived read come from the row alone

-- The history tables have no entity foreign key: they outlive the entity (S40 §7.1). profile_id is
-- RESTRICT, as on the snapshots. Every table is insert-only and purged by RiskIndexRetentionJob alone.

ALTER TABLE `risk_index_snapshots`
    ADD COLUMN IF NOT EXISTS `category_state` int(11) NULL,
    ADD COLUMN IF NOT EXISTS `chips` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    ADD COLUMN IF NOT EXISTS `display_state` int(11) NULL,
    ADD COLUMN IF NOT EXISTS `markers` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `risk_index_object_days`
    ADD COLUMN IF NOT EXISTS `cleared_r` double NOT NULL DEFAULT 0.0,
    ADD COLUMN IF NOT EXISTS `coverage_effect` double NOT NULL DEFAULT 1.0,
    ADD COLUMN IF NOT EXISTS `freshness` double NOT NULL DEFAULT 1.0,
    ADD COLUMN IF NOT EXISTS `r_max` double NOT NULL DEFAULT 1.0;

CREATE TABLE IF NOT EXISTS `risk_index_class_days` (
    `id` bigint(20) NOT NULL AUTO_INCREMENT,
    `snapshot_date` date NOT NULL,
    `profile_id` int(11) NOT NULL,
    `node_entity_id` int(11) NULL,
    `tab_group` int(11) NOT NULL,
    `category` int(11) NOT NULL,
    `value` double NOT NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_index_class_days_category` CHECK (`category` >= 1 AND `category` <= 6),
    CONSTRAINT `ck_risk_index_class_days_tab_group` CHECK (`tab_group` >= 1 AND `tab_group` <= 5),
    CONSTRAINT `ck_risk_index_class_days_value` CHECK (`value` >= 0 AND `value` <= 100),
    CONSTRAINT `fk_risk_index_class_days_profile_id` FOREIGN KEY (`profile_id`) REFERENCES `risk_index_profiles` (`id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_index_incident_closures` (
    `incident_id` int(11) NOT NULL,
    `closed_at` datetime NOT NULL,
    `frozen_on` date NOT NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`incident_id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_index_node_members` (
    `id` bigint(20) NOT NULL AUTO_INCREMENT,
    `snapshot_date` date NOT NULL,
    `profile_id` int(11) NOT NULL,
    `node_entity_id` int(11) NOT NULL,
    `member_kind` int(11) NOT NULL,
    `member_entity_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_index_node_members_kind` CHECK (`member_kind` >= 1 AND `member_kind` <= 2),
    CONSTRAINT `ck_risk_index_node_members_member` CHECK (`member_kind` = 2 OR `member_entity_id` IS NOT NULL),
    CONSTRAINT `fk_risk_index_node_members_profile_id` FOREIGN KEY (`profile_id`) REFERENCES `risk_index_profiles` (`id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_index_object_attributions` (
    `id` bigint(20) NOT NULL AUTO_INCREMENT,
    `snapshot_date` date NOT NULL,
    `profile_id` int(11) NOT NULL,
    `object_kind` int(11) NOT NULL,
    `object_ref` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `entity_id` int(11) NULL,
    `scope_entity_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_risk_index_object_attributions_profile_id` FOREIGN KEY (`profile_id`) REFERENCES `risk_index_profiles` (`id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- MariaDB has no ADD CONSTRAINT ... CHECK ... IF NOT EXISTS, so each CHECK on a table of 106 is a probe
-- (the pattern of Structure/83.sql). No row of 106 violates them: the new columns are null or defaulted.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
                   WHERE CONSTRAINT_SCHEMA = DATABASE()
                     AND TABLE_NAME = 'risk_index_snapshots'
                     AND CONSTRAINT_NAME = 'ck_risk_index_snapshots_category_state') > 0,
                 'DO 0',
                 'ALTER TABLE `risk_index_snapshots` ADD CONSTRAINT `ck_risk_index_snapshots_category_state` CHECK (`category_state` IS NULL OR (`category` <> 0 AND `category_state` >= 1 AND `category_state` <= 4))');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
                   WHERE CONSTRAINT_SCHEMA = DATABASE()
                     AND TABLE_NAME = 'risk_index_snapshots'
                     AND CONSTRAINT_NAME = 'ck_risk_index_snapshots_display_state') > 0,
                 'DO 0',
                 'ALTER TABLE `risk_index_snapshots` ADD CONSTRAINT `ck_risk_index_snapshots_display_state` CHECK (`display_state` IS NULL OR (`category` = 0 AND `display_state` >= 1 AND `display_state` <= 4))');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
                   WHERE CONSTRAINT_SCHEMA = DATABASE()
                     AND TABLE_NAME = 'risk_index_object_days'
                     AND CONSTRAINT_NAME = 'ck_risk_index_object_days_engine') > 0,
                 'DO 0',
                 'ALTER TABLE `risk_index_object_days` ADD CONSTRAINT `ck_risk_index_object_days_engine` CHECK (`freshness` >= 0 AND `freshness` <= 1 AND `coverage_effect` >= 0 AND `coverage_effect` <= 1 AND `r_max` >= 0 AND `cleared_r` >= 0 AND `cleared_r` <= 1)');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

CREATE INDEX IF NOT EXISTS `idx_risk_index_class_days_node_tab_date` ON `risk_index_class_days` (`node_entity_id`, `tab_group`, `snapshot_date`);
CREATE INDEX IF NOT EXISTS `idx_risk_index_class_days_profile_id` ON `risk_index_class_days` (`profile_id`);
CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_index_class_days_date_profile_node_tab_category` ON `risk_index_class_days` (`snapshot_date`, `profile_id`, `node_entity_id`, `tab_group`, `category`);

CREATE INDEX IF NOT EXISTS `idx_risk_index_node_members_date_profile_node` ON `risk_index_node_members` (`snapshot_date`, `profile_id`, `node_entity_id`);
CREATE INDEX IF NOT EXISTS `idx_risk_index_node_members_profile_id` ON `risk_index_node_members` (`profile_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_index_object_attributions_date_profile_entity` ON `risk_index_object_attributions` (`snapshot_date`, `profile_id`, `entity_id`);
CREATE INDEX IF NOT EXISTS `idx_risk_index_object_attributions_profile_id` ON `risk_index_object_attributions` (`profile_id`);
CREATE INDEX IF NOT EXISTS `idx_risk_index_object_attributions_scope_entity_id` ON `risk_index_object_attributions` (`scope_entity_id`);
