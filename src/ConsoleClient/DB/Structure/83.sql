-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 4 milestone 4.6 -- Jira Service Management and Assets.
--
--   jira_connection_settings         The Jira facet of an issue-tracker connection: deployment kind,
--                                    service desk, Assets workspace and schema. A 1:1 extension
--                                    table rather than columns on issue_tracker_connections, which
--                                    three providers with no service desk and no CMDB share.
--   jira_queue_imports               Which service-desk queues feed the mirror. The queues
--                                    themselves are never mirrored -- a queue is a saved JQL filter
--                                    whose membership changes on every triage action.
--   jira_service_requests            The mirror of the customer requests NetRisk cares about.
--   jira_request_slas                One row per SLA cycle per metric. Columns rather than a blob,
--                                    because "what breaches this week" has to be a query; and a row
--                                    per *cycle*, because a reopened request starts a second cycle
--                                    of the same metric and collapsing them loses the first breach.
--   jira_field_mappings              NetRisk field -> Jira field, including custom fields.
--   jira_object_mappings             Assets object type -> NetRisk record kind.
--   jira_object_attribute_mappings   Assets attribute -> NetRisk field. The object mapping's detail.
--   jira_asset_objects               The imported register, and the audit of what each object
--                                    produced -- including the ones that resolved to nothing.
--   hosts.environment / .owner       What a CMDB knows about a machine that no scanner does.
--   finding_issue_links              Widened from findings to findings, incidents and risks.
--
-- Every statement is additive. Nothing is dropped and nothing is renamed, so this version has no
-- destructive gate and needs no observation window.

-- ---------------------------------------------------------------------------------------------
-- hosts: the two CMDB columns.
-- ---------------------------------------------------------------------------------------------

ALTER TABLE `hosts` ADD COLUMN IF NOT EXISTS `environment` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `hosts` ADD COLUMN IF NOT EXISTS `owner` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

-- ---------------------------------------------------------------------------------------------
-- finding_issue_links: a ticket may now hang off a finding, an incident or a risk.
--
-- Three real foreign keys plus a discriminator, and not a polymorphic (kind, id) pair: a
-- polymorphic id cannot carry a foreign key, so deleting a risk would leave a link pointing at
-- nothing and the existing ON DELETE CASCADE would stop working.
--
-- target_kind defaults to 1 (Finding), which is what makes this additive: every row written before
-- 4.6 is a finding link and reads correctly with no backfill. MODIFY is naturally idempotent, so it
-- needs no guard -- applying it twice leaves the same nullable column.
-- ---------------------------------------------------------------------------------------------

ALTER TABLE `finding_issue_links` MODIFY COLUMN `vulnerability_id` int(11) NULL;

ALTER TABLE `finding_issue_links` ADD COLUMN IF NOT EXISTS `incident_id` int(11) NULL;

ALTER TABLE `finding_issue_links` ADD COLUMN IF NOT EXISTS `risk_id` int(11) NULL;

ALTER TABLE `finding_issue_links` ADD COLUMN IF NOT EXISTS `target_kind` int(11) NOT NULL DEFAULT 1;

CREATE INDEX IF NOT EXISTS `idx_finding_issue_links_incident_id` ON `finding_issue_links` (`incident_id`);

CREATE INDEX IF NOT EXISTS `idx_finding_issue_links_risk_id` ON `finding_issue_links` (`risk_id`);

ALTER TABLE `finding_issue_links` ADD CONSTRAINT `fk_finding_issue_links_incident_id` FOREIGN KEY IF NOT EXISTS (`incident_id`) REFERENCES `incidents` (`Id`) ON DELETE CASCADE;

ALTER TABLE `finding_issue_links` ADD CONSTRAINT `fk_finding_issue_links_risk_id` FOREIGN KEY IF NOT EXISTS (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE;

-- Exactly one target, enforced by the database as well as by FindingIssueLink.Validate().
--
-- MariaDB has no ADD CONSTRAINT ... CHECK ... IF NOT EXISTS, so the guard is a probe against
-- information_schema. Existing rows all carry a vulnerability_id and no siblings, so the sum is 1
-- for every one of them and the constraint applies without a backfill.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
                   WHERE CONSTRAINT_SCHEMA = DATABASE()
                     AND TABLE_NAME = 'finding_issue_links'
                     AND CONSTRAINT_NAME = 'ck_finding_issue_links_one_target') > 0,
                 'DO 0',
                 'ALTER TABLE `finding_issue_links` ADD CONSTRAINT `ck_finding_issue_links_one_target` CHECK (((`vulnerability_id` IS NOT NULL) + (`incident_id` IS NOT NULL) + (`risk_id` IS NOT NULL)) = 1)');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- ---------------------------------------------------------------------------------------------
-- The Jira facet.
-- ---------------------------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS `jira_connection_settings` (
    `connection_id` int(11) NOT NULL,
    `deployment` int(11) NOT NULL,
    `jsm_enabled` tinyint(1) NOT NULL,
    `service_desk_id` int(11) NULL,
    `service_desk_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `request_type_filter` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `import_slas` tinyint(1) NOT NULL,
    `sla_breach_notifications` tinyint(1) NOT NULL,
    `default_link_target_kind` int(11) NOT NULL,
    `last_jsm_sync_at` datetime NULL,
    `assets_enabled` tinyint(1) NOT NULL,
    `assets_workspace_id` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `assets_schema_id` int(11) NULL,
    `assets_schema_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `last_assets_sync_at` datetime NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`connection_id`),
    CONSTRAINT `fk_jira_connection_settings_connection_id` FOREIGN KEY (`connection_id`) REFERENCES `issue_tracker_connections` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `jira_queue_imports` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `connection_id` int(11) NOT NULL,
    `service_desk_id` int(11) NOT NULL,
    `queue_id` int(11) NOT NULL,
    `queue_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `enabled` tinyint(1) NOT NULL,
    `link_target_kind` int(11) NULL,
    `max_requests` int(11) NOT NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_jira_queue_imports_connection_id` FOREIGN KEY (`connection_id`) REFERENCES `jira_connection_settings` (`connection_id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_jira_queue_imports_connection_queue` ON `jira_queue_imports` (`connection_id`, `queue_id`);

-- ---------------------------------------------------------------------------------------------
-- The Service Management mirror.
-- ---------------------------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS `jira_service_requests` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `connection_id` int(11) NOT NULL,
    `issue_key` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `issue_id` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `service_desk_id` int(11) NULL,
    `request_type_id` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `request_type_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `summary` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `status_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `status_category` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `reporter_account_id` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `reporter_display_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `organization_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `priority_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `assignee_display_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at_remote` datetime NULL,
    `updated_at_remote` datetime NULL,
    `is_closed` tinyint(1) NOT NULL,
    `request_url` varchar(1024) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `first_seen_at` datetime NOT NULL,
    `last_synced_at` datetime NULL,
    `sync_error` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_jira_service_requests_connection_id` FOREIGN KEY (`connection_id`) REFERENCES `issue_tracker_connections` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_jira_service_requests_connection_key` ON `jira_service_requests` (`connection_id`, `issue_key`);

CREATE INDEX IF NOT EXISTS `idx_jira_service_requests_connection_closed` ON `jira_service_requests` (`connection_id`, `is_closed`);

CREATE INDEX IF NOT EXISTS `idx_jira_service_requests_updated_at_remote` ON `jira_service_requests` (`updated_at_remote`);

CREATE TABLE IF NOT EXISTS `jira_request_slas` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `request_id` int(11) NOT NULL,
    `metric_id` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `metric_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `is_ongoing` tinyint(1) NOT NULL,
    `breached` tinyint(1) NOT NULL,
    `paused` tinyint(1) NOT NULL,
    `goal_duration_ms` bigint(20) NULL,
    `elapsed_ms` bigint(20) NULL,
    `remaining_ms` bigint(20) NULL,
    `cycle_start_at` datetime NULL,
    `cycle_stop_at` datetime NULL,
    `captured_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_jira_request_slas_request_id` FOREIGN KEY (`request_id`) REFERENCES `jira_service_requests` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_jira_request_slas_request_metric_cycle` ON `jira_request_slas` (`request_id`, `metric_name`, `cycle_start_at`);

CREATE INDEX IF NOT EXISTS `idx_jira_request_slas_breached_ongoing` ON `jira_request_slas` (`breached`, `is_ongoing`);

-- ---------------------------------------------------------------------------------------------
-- The three configurable mappings.
-- ---------------------------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS `jira_field_mappings` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `connection_id` int(11) NOT NULL,
    `direction` int(11) NOT NULL,
    `netrisk_field` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `jira_field_id` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `jira_field_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `jira_field_type` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `transform` int(11) NOT NULL,
    `constant_value` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `enabled` tinyint(1) NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_jira_field_mappings_connection_id` FOREIGN KEY (`connection_id`) REFERENCES `issue_tracker_connections` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_jira_field_mappings_connection_direction_field` ON `jira_field_mappings` (`connection_id`, `direction`, `jira_field_id`);

CREATE TABLE IF NOT EXISTS `jira_object_mappings` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `connection_id` int(11) NOT NULL,
    `object_type_id` int(11) NOT NULL,
    `object_type_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `target_kind` int(11) NOT NULL,
    `aql_filter` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `match_strategy` int(11) NOT NULL,
    `enabled` tinyint(1) NOT NULL,
    `create_missing` tinyint(1) NOT NULL,
    `update_existing` tinyint(1) NOT NULL,
    `deactivate_missing` tinyint(1) NOT NULL,
    `last_imported_at` datetime NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_jira_object_mappings_connection_id` FOREIGN KEY (`connection_id`) REFERENCES `issue_tracker_connections` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_jira_object_mappings_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_jira_object_mappings_connection_object_type` ON `jira_object_mappings` (`connection_id`, `object_type_id`);

CREATE INDEX IF NOT EXISTS `idx_jira_object_mappings_created_by_id` ON `jira_object_mappings` (`created_by_id`);

CREATE TABLE IF NOT EXISTS `jira_object_attribute_mappings` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `mapping_id` int(11) NOT NULL,
    `source_attribute_id` int(11) NULL,
    `source_attribute_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `target_field` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `transform` int(11) NOT NULL,
    `is_identity` tinyint(1) NOT NULL,
    `constant_value` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `sort_order` int(11) NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_jira_object_attribute_mappings_mapping_id` FOREIGN KEY (`mapping_id`) REFERENCES `jira_object_mappings` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_jira_object_attribute_mappings_mapping_target` ON `jira_object_attribute_mappings` (`mapping_id`, `target_field`);

-- ---------------------------------------------------------------------------------------------
-- The imported register, and the audit of what each object produced.
--
-- The two target foreign keys are ON DELETE SET NULL rather than CASCADE: deleting a host must not
-- delete the record that Jira reported it. "This Assets object mapped to a host that has since been
-- removed" is exactly the row somebody needs when the machine reappears next import.
-- ---------------------------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS `jira_asset_objects` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `connection_id` int(11) NOT NULL,
    `object_id` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `object_key` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `object_type_id` int(11) NULL,
    `object_type_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `label` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `mapped_name` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `mapped_owner` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `mapped_environment` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `mapped_active` tinyint(1) NULL,
    `attributes_json` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `target_kind` int(11) NOT NULL,
    `target_host_id` int(11) NULL,
    `target_entity_id` int(11) NULL,
    `match_reason` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at_remote` datetime NULL,
    `updated_at_remote` datetime NULL,
    `first_seen_at` datetime NOT NULL,
    `last_synced_at` datetime NULL,
    `import_error` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_jira_asset_objects_connection_id` FOREIGN KEY (`connection_id`) REFERENCES `issue_tracker_connections` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_jira_asset_objects_target_entity_id` FOREIGN KEY (`target_entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `fk_jira_asset_objects_target_host_id` FOREIGN KEY (`target_host_id`) REFERENCES `hosts` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_jira_asset_objects_connection_object` ON `jira_asset_objects` (`connection_id`, `object_id`);

CREATE INDEX IF NOT EXISTS `idx_jira_asset_objects_target_host_id` ON `jira_asset_objects` (`target_host_id`);

CREATE INDEX IF NOT EXISTS `idx_jira_asset_objects_target_entity_id` ON `jira_asset_objects` (`target_entity_id`);
