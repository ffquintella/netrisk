-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so this script carries no
-- transaction; every statement is guarded instead, and applying the version again converges on the
-- same schema.

-- M58, T291 (S39 §3.2 CMDB-INET / CMDB-REL, §5.3) -- what the CMDB says about a host that the risk index reads.
-- EF migration 20261118180000_CmdbExposureAndApplicationLinks.

-- hosts: whether the host faces the internet (NULL = nobody said), who said so (manual / cmdb) and when (UTC). A CMDB
-- value counts for m_net only inside the CMDB-INET window; a manual one does not age and no import overwrites it.
ALTER TABLE `hosts` ADD COLUMN IF NOT EXISTS `internet_facing` tinyint(1) NULL;
ALTER TABLE `hosts` ADD COLUMN IF NOT EXISTS `internet_facing_source` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;
ALTER TABLE `hosts` ADD COLUMN IF NOT EXISTS `internet_facing_seen_at` datetime NULL;

-- host_application_links: "this host runs that application" (CMDB-REL), one row per pair. source is cmdb or manual; an
-- import refreshes last_seen_at on its own rows and never touches a manual one. Host, application entity and reporting
-- connection are CASCADE: a relation outlives none of them (a manual row has no connection).
CREATE TABLE IF NOT EXISTS `host_application_links` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `host_id` int(11) NOT NULL,
    `entity_id` int(11) NOT NULL,
    `source` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `connection_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    `last_seen_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_host_application_links_connection_id` FOREIGN KEY (`connection_id`) REFERENCES `issue_tracker_connections` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_host_application_links_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `fk_host_application_links_host_id` FOREIGN KEY (`host_id`) REFERENCES `hosts` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_host_application_links_connection_id` ON `host_application_links` (`connection_id`);

CREATE INDEX IF NOT EXISTS `idx_host_application_links_entity_id` ON `host_application_links` (`entity_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_host_application_links_host_id_entity_id` ON `host_application_links` (`host_id`, `entity_id`);
