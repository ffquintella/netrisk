-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 9, Stage 9.1 (S41, M39) -- the risk linkage chain:
-- strategic objective -> business process -> IT service -> data -> asset.
--
--   risk_chain_links  One row per link from a risk to one node of the chain: an entity of a chain
--                     type (entity_id) or a host (host_id), never both and never neither
--                     (ck_risk_chain_links_one_target). chain_level is derived from the target by
--                     the service, never taken from a request. origin is 1 = Declared (written
--                     through /RiskChain) or 2 = Legacy (mirrored from risk_to_entity, which stays
--                     the source of truth for those rows while the two coexist).
--
-- risks.entity_id is not touched: it is the scope column (visibility, appetite, campaigns), and the
-- chain is identification. Mixing the two is the very defect this stage closes.
--
-- Version 88 rather than the 88/89 S40 §7.6 had reserved for M52/M53 and M56/M57: this stage merged
-- first, and the reservation shifts to 89/90 by amendment (S41 §5, R4; S40 §7.6).
--
-- A new table, nothing dropped or renamed: no destructive gate, no observation window. Every
-- statement uses a native guard, so no information_schema probe is needed.

CREATE TABLE IF NOT EXISTS `risk_chain_links` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `risk_id` int(11) NOT NULL,
    `chain_level` int(11) NOT NULL,
    `entity_id` int(11) NULL,
    `host_id` int(11) NULL,
    `origin` int(11) NOT NULL DEFAULT 1,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_risk_chain_links_one_target` CHECK (((`entity_id` IS NOT NULL) + (`host_id` IS NOT NULL)) = 1),
    CONSTRAINT `fk_risk_chain_links_risk_id` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_risk_chain_links_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `fk_risk_chain_links_host_id` FOREIGN KEY (`host_id`) REFERENCES `hosts` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `fk_risk_chain_links_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- NULLs never collide in a MariaDB unique index, so host rows (entity_id NULL) do not conflict in
-- the first index, nor entity rows (host_id NULL) in the second.
CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_chain_links_risk_id_entity_id` ON `risk_chain_links` (`risk_id`, `entity_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_chain_links_risk_id_host_id` ON `risk_chain_links` (`risk_id`, `host_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_chain_links_entity_id` ON `risk_chain_links` (`entity_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_chain_links_host_id` ON `risk_chain_links` (`host_id`);

CREATE INDEX IF NOT EXISTS `idx_risk_chain_links_created_by_id` ON `risk_chain_links` (`created_by_id`);
