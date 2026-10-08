-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 9 Stage 9.11 (M49, T206-T210, S52) -- the LGPD data catalogue: legal basis, purpose,
-- retention, location, international transfer and the RIPD (DPIA).
--
--   legal_requirements         Laws, regulations, contracts and internal norms, cited by the
--                              catalogue and linked to risks (a contract may name its third party).
--   data_catalogue_entries     The catalogue of one organizationData node: personal-data category,
--                              minors, large volume, strategic research, retention, transfer.
--   data_catalogue_purposes    Its purposes, each with its LGPD legal basis (art. 7 or art. 11).
--   data_catalogue_locations   Where the data is, by country and use.
--   dpias                      The RIPD as an artifact: approved by a person, frozen, never deleted.
--   dpia_links                 What a RIPD covers: data records and business processes.
--   risk_legal_requirements    The legal and contractual requirements of a risk, as links.
--
-- A catalogue of KINDS of data: no column below holds a data subject's value.
--
-- New tables only -- no ALTER on an existing table. Created in reference order: legal_requirements
-- (third_parties, user) before everything that cites it; data_catalogue_entries (entities) before its
-- purposes and locations; dpias before its links; risk_legal_requirements (risks) last. The purposes,
-- the retentions and the risk links reference a requirement with ON DELETE RESTRICT: a requirement in
-- use is not deleted (S52 section 4.9).
--
-- Version 100 rather than the 100/101 S40 section 7.6 had reserved for M52 and M56/M57: this change
-- merged first, so S40 shifts its scripts to 101 and 102 (the rule #79, #80 and Stages 9.1-9.10 followed).

CREATE TABLE IF NOT EXISTS `legal_requirements` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `code` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `title` varchar(300) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `kind` int(11) NOT NULL,
    `description` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `reference` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `third_party_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    `updated_at` datetime NULL,
    `updated_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_legal_requirements_kind` CHECK (`kind` >= 1 AND `kind` <= 4),
    CONSTRAINT `fk_legal_requirements_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_legal_requirements_third_party_id` FOREIGN KEY (`third_party_id`) REFERENCES `third_parties` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_legal_requirements_updated_by_id` FOREIGN KEY (`updated_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `data_catalogue_entries` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `entity_id` int(11) NOT NULL,
    `personal_data` int(11) NULL,
    `involves_minors` tinyint(1) NULL,
    `large_volume` tinyint(1) NOT NULL,
    `strategic_research` tinyint(1) NOT NULL,
    `data_subjects` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `data_categories` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `retention_period_months` int(11) NULL,
    `retention_trigger` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `retention_basis` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `retention_requirement_id` int(11) NULL,
    `retention_review_due_at` datetime NULL,
    `retention_reviewed_at` datetime NULL,
    `international_transfer` tinyint(1) NULL,
    `transfer_mechanism` int(11) NULL,
    `notes` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    `updated_at` datetime NULL,
    `updated_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_data_catalogue_entries_personal_data` CHECK (`personal_data` IS NULL OR (`personal_data` >= 1 AND `personal_data` <= 4)),
    CONSTRAINT `ck_data_catalogue_entries_retention_period` CHECK (`retention_period_months` IS NULL OR (`retention_period_months` >= 0 AND `retention_period_months` <= 1200)),
    CONSTRAINT `ck_data_catalogue_entries_transfer_mechanism` CHECK (`transfer_mechanism` IS NULL OR (`transfer_mechanism` >= 1 AND `transfer_mechanism` <= 12)),
    CONSTRAINT `fk_data_catalogue_entries_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_data_catalogue_entries_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `fk_data_catalogue_entries_retention_requirement_id` FOREIGN KEY (`retention_requirement_id`) REFERENCES `legal_requirements` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_data_catalogue_entries_updated_by_id` FOREIGN KEY (`updated_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `data_catalogue_purposes` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `entry_id` int(11) NOT NULL,
    `purpose` varchar(300) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `legal_basis` int(11) NULL,
    `legal_requirement_id` int(11) NULL,
    `basis_reference` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_data_catalogue_purposes_legal_basis` CHECK (`legal_basis` IS NULL OR (`legal_basis` >= 1 AND `legal_basis` <= 18)),
    CONSTRAINT `fk_data_catalogue_purposes_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_data_catalogue_purposes_entry_id` FOREIGN KEY (`entry_id`) REFERENCES `data_catalogue_entries` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_data_catalogue_purposes_legal_requirement_id` FOREIGN KEY (`legal_requirement_id`) REFERENCES `legal_requirements` (`id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `data_catalogue_locations` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `entry_id` int(11) NOT NULL,
    `country` varchar(2) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `region` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `purpose` int(11) NOT NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_data_catalogue_locations_purpose` CHECK (`purpose` >= 1 AND `purpose` <= 4),
    CONSTRAINT `fk_data_catalogue_locations_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_data_catalogue_locations_entry_id` FOREIGN KEY (`entry_id`) REFERENCES `data_catalogue_entries` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `dpias` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `title` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `status` int(11) NOT NULL,
    `summary` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `document_reference` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `residual_risk` int(11) NULL,
    `performed_at` datetime NULL,
    `next_review_due_at` datetime NULL,
    `approved_at` datetime NULL,
    `approved_by_id` int(11) NULL,
    `retired_at` datetime NULL,
    `retired_by_id` int(11) NULL,
    `retire_reason` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    `updated_at` datetime NULL,
    `updated_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_dpias_approved` CHECK (`status` <> 2 OR `approved_at` IS NOT NULL),
    CONSTRAINT `ck_dpias_residual_risk` CHECK (`residual_risk` IS NULL OR (`residual_risk` >= 1 AND `residual_risk` <= 3)),
    CONSTRAINT `ck_dpias_retired` CHECK ((`status` = 3 AND `retired_at` IS NOT NULL AND `retire_reason` IS NOT NULL) OR (`status` <> 3 AND `retired_at` IS NULL)),
    CONSTRAINT `ck_dpias_status` CHECK (`status` >= 1 AND `status` <= 3),
    CONSTRAINT `fk_dpias_approved_by_id` FOREIGN KEY (`approved_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_dpias_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_dpias_retired_by_id` FOREIGN KEY (`retired_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_dpias_updated_by_id` FOREIGN KEY (`updated_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `dpia_links` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `dpia_id` int(11) NOT NULL,
    `entity_id` int(11) NOT NULL,
    `kind` int(11) NOT NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_dpia_links_kind` CHECK (`kind` >= 1 AND `kind` <= 2),
    CONSTRAINT `fk_dpia_links_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_dpia_links_dpia_id` FOREIGN KEY (`dpia_id`) REFERENCES `dpias` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_dpia_links_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_legal_requirements` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `risk_id` int(11) NOT NULL,
    `legal_requirement_id` int(11) NOT NULL,
    `note` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_risk_legal_requirements_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_risk_legal_requirements_legal_requirement_id` FOREIGN KEY (`legal_requirement_id`) REFERENCES `legal_requirements` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_risk_legal_requirements_risk_id` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_data_catalogue_entries_created_by_id` ON `data_catalogue_entries` (`created_by_id`);
CREATE INDEX IF NOT EXISTS `idx_data_catalogue_entries_retention_requirement_id` ON `data_catalogue_entries` (`retention_requirement_id`);
CREATE INDEX IF NOT EXISTS `idx_data_catalogue_entries_updated_by_id` ON `data_catalogue_entries` (`updated_by_id`);
CREATE UNIQUE INDEX IF NOT EXISTS `uq_data_catalogue_entries_entity_id` ON `data_catalogue_entries` (`entity_id`);
CREATE INDEX IF NOT EXISTS `idx_data_catalogue_locations_created_by_id` ON `data_catalogue_locations` (`created_by_id`);
CREATE UNIQUE INDEX IF NOT EXISTS `uq_data_catalogue_locations_entry_id_country_purpose` ON `data_catalogue_locations` (`entry_id`, `country`, `purpose`);
CREATE INDEX IF NOT EXISTS `idx_data_catalogue_purposes_created_by_id` ON `data_catalogue_purposes` (`created_by_id`);
CREATE INDEX IF NOT EXISTS `idx_data_catalogue_purposes_legal_requirement_id` ON `data_catalogue_purposes` (`legal_requirement_id`);
CREATE UNIQUE INDEX IF NOT EXISTS `uq_data_catalogue_purposes_entry_id_purpose` ON `data_catalogue_purposes` (`entry_id`, `purpose`);
CREATE INDEX IF NOT EXISTS `idx_dpia_links_created_by_id` ON `dpia_links` (`created_by_id`);
CREATE INDEX IF NOT EXISTS `idx_dpia_links_entity_id` ON `dpia_links` (`entity_id`);
CREATE UNIQUE INDEX IF NOT EXISTS `uq_dpia_links_dpia_id_entity_id` ON `dpia_links` (`dpia_id`, `entity_id`);
CREATE INDEX IF NOT EXISTS `idx_dpias_approved_by_id` ON `dpias` (`approved_by_id`);
CREATE INDEX IF NOT EXISTS `idx_dpias_created_by_id` ON `dpias` (`created_by_id`);
CREATE INDEX IF NOT EXISTS `idx_dpias_retired_by_id` ON `dpias` (`retired_by_id`);
CREATE INDEX IF NOT EXISTS `idx_dpias_status` ON `dpias` (`status`);
CREATE INDEX IF NOT EXISTS `idx_dpias_updated_by_id` ON `dpias` (`updated_by_id`);
CREATE INDEX IF NOT EXISTS `idx_legal_requirements_created_by_id` ON `legal_requirements` (`created_by_id`);
CREATE INDEX IF NOT EXISTS `idx_legal_requirements_third_party_id` ON `legal_requirements` (`third_party_id`);
CREATE INDEX IF NOT EXISTS `idx_legal_requirements_updated_by_id` ON `legal_requirements` (`updated_by_id`);
CREATE UNIQUE INDEX IF NOT EXISTS `uq_legal_requirements_code` ON `legal_requirements` (`code`);
CREATE INDEX IF NOT EXISTS `idx_risk_legal_requirements_created_by_id` ON `risk_legal_requirements` (`created_by_id`);
CREATE INDEX IF NOT EXISTS `idx_risk_legal_requirements_legal_requirement_id` ON `risk_legal_requirements` (`legal_requirement_id`);
CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_legal_requirements_risk_id_legal_requirement_id` ON `risk_legal_requirements` (`risk_id`, `legal_requirement_id`);
