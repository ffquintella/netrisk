-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 9, Stage 9.3 (S43, M41) -- business impact analysis: MTPD/MAO, RTO, RPO, cascading
-- dependencies and restoration tests.
--
--   business_impact_analyses  One BIA per business process or IT service (uq on entity_id). The three
--                             objectives are whole minutes; NULL is "not declared", 0 is a value.
--                             At least one is set, none is negative, and the RTO never exceeds the
--                             MTPD on the same row.
--   bia_dependencies          dependent_entity_id depends on provider_entity_id. Cycles are allowed
--                             (and reported by the service); a self-dependency is not. The table is
--                             named bia_ because "continuity_dependencies" makes the unique index name
--                             65 characters, past MariaDB's 64.
--   restoration_tests         Insert-only evidence of a restoration test; voided with a reason, never
--                             edited or deleted. A successful test measured something; a void is
--                             complete or absent.
--
-- Every FK to entities cascades, as risk_chain_links does (S41 D11): deleting the node deletes its
-- continuity data. Authors are SET NULL when the user goes.
--
-- Version 90 rather than the 90/91 S40 §7.6 had reserved for M52/M53 and M56/M57: this stage merged
-- first, so S40 shifts its scripts to 91 and 92 (same rule as Stages 9.1 and 9.2).

CREATE TABLE IF NOT EXISTS `business_impact_analyses` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `entity_id` int(11) NOT NULL,
    `mtpd_minutes` int(11) NULL,
    `rto_minutes` int(11) NULL,
    `rpo_minutes` int(11) NULL,
    `assessed_at` datetime NOT NULL,
    `notes` text NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    `updated_at` datetime NULL,
    `updated_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_business_impact_analyses_declared` CHECK (`mtpd_minutes` IS NOT NULL OR `rto_minutes` IS NOT NULL OR `rpo_minutes` IS NOT NULL),
    CONSTRAINT `ck_business_impact_analyses_non_negative` CHECK ((`mtpd_minutes` IS NULL OR `mtpd_minutes` >= 0) AND (`rto_minutes` IS NULL OR `rto_minutes` >= 0) AND (`rpo_minutes` IS NULL OR `rpo_minutes` >= 0)),
    CONSTRAINT `ck_business_impact_analyses_rto_within_mtpd` CHECK (`rto_minutes` IS NULL OR `mtpd_minutes` IS NULL OR `rto_minutes` <= `mtpd_minutes`),
    CONSTRAINT `fk_business_impact_analyses_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `fk_business_impact_analyses_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_business_impact_analyses_updated_by_id` FOREIGN KEY (`updated_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_business_impact_analyses_entity_id` ON `business_impact_analyses` (`entity_id`);
CREATE INDEX IF NOT EXISTS `idx_business_impact_analyses_created_by_id` ON `business_impact_analyses` (`created_by_id`);
CREATE INDEX IF NOT EXISTS `idx_business_impact_analyses_updated_by_id` ON `business_impact_analyses` (`updated_by_id`);

CREATE TABLE IF NOT EXISTS `bia_dependencies` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `dependent_entity_id` int(11) NOT NULL,
    `provider_entity_id` int(11) NOT NULL,
    `description` varchar(500) NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_bia_dependencies_not_self` CHECK (`dependent_entity_id` <> `provider_entity_id`),
    CONSTRAINT `fk_bia_dependencies_dependent_entity_id` FOREIGN KEY (`dependent_entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `fk_bia_dependencies_provider_entity_id` FOREIGN KEY (`provider_entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `fk_bia_dependencies_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_bia_dependencies_dependent_entity_id_provider_entity_id` ON `bia_dependencies` (`dependent_entity_id`, `provider_entity_id`);
CREATE INDEX IF NOT EXISTS `idx_bia_dependencies_provider_entity_id` ON `bia_dependencies` (`provider_entity_id`);
CREATE INDEX IF NOT EXISTS `idx_bia_dependencies_created_by_id` ON `bia_dependencies` (`created_by_id`);

CREATE TABLE IF NOT EXISTS `restoration_tests` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `entity_id` int(11) NOT NULL,
    `tested_at` datetime NOT NULL,
    `outcome` int(11) NOT NULL,
    `achieved_rto_minutes` int(11) NULL,
    `achieved_rpo_minutes` int(11) NULL,
    `declared_rto_minutes` int(11) NULL,
    `declared_rpo_minutes` int(11) NULL,
    `evidence_reference` varchar(500) NULL,
    `notes` text NULL,
    `created_at` datetime NOT NULL,
    `recorded_by_id` int(11) NULL,
    `voided_at` datetime NULL,
    `voided_by_id` int(11) NULL,
    `void_reason` varchar(500) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_restoration_tests_non_negative` CHECK ((`achieved_rto_minutes` IS NULL OR `achieved_rto_minutes` >= 0) AND (`achieved_rpo_minutes` IS NULL OR `achieved_rpo_minutes` >= 0)),
    CONSTRAINT `ck_restoration_tests_measured` CHECK (`outcome` <> 1 OR `achieved_rto_minutes` IS NOT NULL OR `achieved_rpo_minutes` IS NOT NULL),
    CONSTRAINT `ck_restoration_tests_void_complete` CHECK ((`voided_at` IS NULL) = (`void_reason` IS NULL)),
    CONSTRAINT `fk_restoration_tests_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `fk_restoration_tests_recorded_by_id` FOREIGN KEY (`recorded_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_restoration_tests_voided_by_id` FOREIGN KEY (`voided_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_restoration_tests_entity_id_tested_at` ON `restoration_tests` (`entity_id`, `tested_at`);
CREATE INDEX IF NOT EXISTS `idx_restoration_tests_recorded_by_id` ON `restoration_tests` (`recorded_by_id`);
CREATE INDEX IF NOT EXISTS `idx_restoration_tests_voided_by_id` ON `restoration_tests` (`voided_by_id`);
