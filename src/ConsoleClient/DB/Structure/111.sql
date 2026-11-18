-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so this script carries no
-- transaction; every statement is guarded instead, and applying the version again converges on the
-- same schema.

-- M55, T263 (S40 §6.6, §6.9, §7.2, §8.6, §8.8) -- the Vision One custom-tag -> entity rules.
-- EF migration 20261118131212_VendorEntityMappings.

-- trendmicro_connections.sync_custom_tags: read each device's assetCustomTags and apply the connection's rules;
-- off by default, because a rule moves who sees a host.
ALTER TABLE `trendmicro_connections` ADD COLUMN IF NOT EXISTS `sync_custom_tags` tinyint(1) NOT NULL DEFAULT 0;

-- host_external_ids: a host's vendor identity and the tags the vendor last reported (S40 §7.2); connection_id is
-- beyond the spec, because the rules are per connection; the host is CASCADE.
CREATE TABLE IF NOT EXISTS `host_external_ids` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `host_id` int(11) NOT NULL,
    `provider` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `connection_id` int(11) NULL,
    `external_id` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `tags` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `first_seen_at` datetime NOT NULL,
    `last_seen_at` datetime NOT NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_host_external_ids_host_id` FOREIGN KEY (`host_id`) REFERENCES `hosts` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- vendor_entity_mappings: provider + connection + kind + value -> entity, with priority and restamp policy; the entity
-- is CASCADE (a rule for a deleted entity attributes nothing), the author SET NULL.
CREATE TABLE IF NOT EXISTS `vendor_entity_mappings` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `provider` int(11) NOT NULL,
    `connection_id` int(11) NOT NULL,
    `match_kind` int(11) NOT NULL,
    `match_value` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `match_label` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `entity_id` int(11) NOT NULL,
    `priority` int(11) NOT NULL,
    `restamp_policy` int(11) NOT NULL,
    `enabled` tinyint(1) NOT NULL,
    `created_by_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_vendor_entity_mappings_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_vendor_entity_mappings_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_host_external_ids_host_id` ON `host_external_ids` (`host_id`);

CREATE INDEX IF NOT EXISTS `idx_host_external_ids_provider_connection_id` ON `host_external_ids` (`provider`, `connection_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_host_external_ids_provider_external_id` ON `host_external_ids` (`provider`, `external_id`);

CREATE INDEX IF NOT EXISTS `idx_vendor_entity_mappings_created_by_id` ON `vendor_entity_mappings` (`created_by_id`);

CREATE INDEX IF NOT EXISTS `idx_vendor_entity_mappings_entity_id` ON `vendor_entity_mappings` (`entity_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_vendor_entity_mappings_provider_connection_kind_value` ON `vendor_entity_mappings` (`provider`, `connection_id`, `match_kind`, `match_value`);
