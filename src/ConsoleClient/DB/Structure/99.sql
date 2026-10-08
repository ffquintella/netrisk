-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 9 Stage 9.10 (M48, T200-T205, S51) -- the third-party register: HECVAT, SBOM,
-- sub-processors, data location, contract terms, concentration and exit plan.
--
--   third_parties                   The supplier as a first-class record, distinct from the generic
--                                   `organization` node: contract, SLA and contracted RTO/RPO,
--                                   vulnerability-fix deadline, right to audit, exit plan, portability,
--                                   and whether it is a cloud or an identity provider.
--   third_party_links               What it supplies or processes: an IT service, a business process, a
--                                   data record (one per supplier and entity; kind derived from the entity).
--   third_party_subprocessors       Its sub-processors (LGPD suboperadores), optionally registered suppliers.
--   third_party_data_locations      Where it keeps or reaches the data (one per country and purpose).
--   third_party_assessments         Each HECVAT the vendor answered, with how many questions it was asked.
--   third_party_assessment_answers  The answers (one per assessment and question id).
--   third_party_sboms               Each SBOM imported (one per supplier and document hash).
--   third_party_sbom_components     Its components.
--
-- New tables only. Created in reference order: third_parties (entities, user) before everything that
-- names it; third_party_assessments before its answers; third_party_sboms before its components. The
-- links, the assessments, the SBOMs and a row naming a supplier as a sub-processor reference it with
-- ON DELETE RESTRICT: a supplier in use is not deleted (S51 section 4.9).
--
-- Version 99 rather than the 99/100 S40 section 7.6 had reserved for M52 and M56/M57: this change
-- merged first, so S40 shifts its scripts to 100 and 101 (the rule #79, #80 and Stages 9.1-9.9 followed).

CREATE TABLE IF NOT EXISTS `third_parties` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `name` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `legal_name` varchar(300) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `tax_id` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `country` varchar(2) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `description` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `website` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `entity_id` int(11) NULL,
    `owner_id` int(11) NULL,
    `status` int(11) NOT NULL,
    `is_cloud_provider` tinyint(1) NOT NULL,
    `is_identity_provider` tinyint(1) NOT NULL,
    `processes_personal_data` tinyint(1) NULL,
    `subprocessors_declared_at` datetime NULL,
    `contract_reference` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `contract_start` datetime NULL,
    `contract_end` datetime NULL,
    `sla_availability_percent` decimal(6,3) NULL,
    `contracted_rto_minutes` int(11) NULL,
    `contracted_rpo_minutes` int(11) NULL,
    `vulnerability_fix_days` int(11) NULL,
    `right_to_audit` tinyint(1) NULL,
    `audit_clause_reference` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `exit_plan` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `exit_plan_reviewed_at` datetime NULL,
    `exit_plan_tested_at` datetime NULL,
    `data_portability` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `terminated_at` datetime NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    `updated_at` datetime NULL,
    `updated_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_third_parties_contract_dates` CHECK (`contract_start` IS NULL OR `contract_end` IS NULL OR `contract_end` >= `contract_start`),
    CONSTRAINT `ck_third_parties_non_negative` CHECK ((`contracted_rto_minutes` IS NULL OR `contracted_rto_minutes` >= 0) AND (`contracted_rpo_minutes` IS NULL OR `contracted_rpo_minutes` >= 0) AND (`vulnerability_fix_days` IS NULL OR `vulnerability_fix_days` >= 0)),
    CONSTRAINT `ck_third_parties_sla_availability` CHECK (`sla_availability_percent` IS NULL OR (`sla_availability_percent` > 0 AND `sla_availability_percent` <= 100)),
    CONSTRAINT `ck_third_parties_status` CHECK (`status` >= 1 AND `status` <= 4),
    CONSTRAINT `ck_third_parties_terminated` CHECK ((`status` = 4 AND `terminated_at` IS NOT NULL) OR (`status` <> 4 AND `terminated_at` IS NULL)),
    CONSTRAINT `fk_third_parties_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_third_parties_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `fk_third_parties_owner_id` FOREIGN KEY (`owner_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_third_parties_updated_by_id` FOREIGN KEY (`updated_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `third_party_assessments` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `third_party_id` int(11) NOT NULL,
    `variant` int(11) NOT NULL,
    `framework_version` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `expected_question_count` int(11) NOT NULL,
    `responded_at` datetime NULL,
    `valid_until` datetime NULL,
    `evidence_reference` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `notes` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `answers_updated_at` datetime NULL,
    `answers_updated_by_id` int(11) NULL,
    `voided_at` datetime NULL,
    `voided_by_id` int(11) NULL,
    `void_reason` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_third_party_assessments_expected_question_count` CHECK (`expected_question_count` >= 1 AND `expected_question_count` <= 2000),
    CONSTRAINT `ck_third_party_assessments_variant` CHECK (`variant` >= 1 AND `variant` <= 4),
    CONSTRAINT `ck_third_party_assessments_voided` CHECK ((`voided_at` IS NULL AND `void_reason` IS NULL) OR (`voided_at` IS NOT NULL AND `void_reason` IS NOT NULL)),
    CONSTRAINT `fk_third_party_assessments_answers_updated_by_id` FOREIGN KEY (`answers_updated_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_third_party_assessments_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_third_party_assessments_third_party_id` FOREIGN KEY (`third_party_id`) REFERENCES `third_parties` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_third_party_assessments_voided_by_id` FOREIGN KEY (`voided_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `third_party_data_locations` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `third_party_id` int(11) NOT NULL,
    `country` varchar(2) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `region` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `purpose` int(11) NOT NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_third_party_data_locations_purpose` CHECK (`purpose` >= 1 AND `purpose` <= 4),
    CONSTRAINT `fk_third_party_data_locations_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_third_party_data_locations_third_party_id` FOREIGN KEY (`third_party_id`) REFERENCES `third_parties` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `third_party_links` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `third_party_id` int(11) NOT NULL,
    `entity_id` int(11) NOT NULL,
    `kind` int(11) NOT NULL,
    `description` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_third_party_links_kind` CHECK (`kind` >= 1 AND `kind` <= 3),
    CONSTRAINT `fk_third_party_links_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_third_party_links_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `fk_third_party_links_third_party_id` FOREIGN KEY (`third_party_id`) REFERENCES `third_parties` (`id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `third_party_sboms` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `third_party_id` int(11) NOT NULL,
    `component_name` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `component_version` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `format` int(11) NOT NULL,
    `spec_version` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `serial_number` varchar(300) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `document_sha256` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `document_size_bytes` int(11) NOT NULL,
    `file_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `component_count` int(11) NOT NULL,
    `uploaded_at` datetime NOT NULL,
    `uploaded_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_third_party_sboms_counts` CHECK (`document_size_bytes` >= 0 AND `component_count` >= 0),
    CONSTRAINT `ck_third_party_sboms_format` CHECK (`format` >= 1 AND `format` <= 2),
    CONSTRAINT `fk_third_party_sboms_third_party_id` FOREIGN KEY (`third_party_id`) REFERENCES `third_parties` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_third_party_sboms_uploaded_by_id` FOREIGN KEY (`uploaded_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `third_party_subprocessors` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `third_party_id` int(11) NOT NULL,
    `name` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `subprocessor_third_party_id` int(11) NULL,
    `service` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `country` varchar(2) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `processes_personal_data` tinyint(1) NOT NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_third_party_subprocessors_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_third_party_subprocessors_subprocessor_third_party_id` FOREIGN KEY (`subprocessor_third_party_id`) REFERENCES `third_parties` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_third_party_subprocessors_third_party_id` FOREIGN KEY (`third_party_id`) REFERENCES `third_parties` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `third_party_assessment_answers` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `assessment_id` int(11) NOT NULL,
    `question_id` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `answer` int(11) NOT NULL,
    `preferred_answer` int(11) NULL,
    `weight` int(11) NOT NULL,
    `critical` tinyint(1) NOT NULL,
    `notes` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_third_party_assessment_answers_answer` CHECK (`answer` >= 1 AND `answer` <= 4),
    CONSTRAINT `ck_third_party_assessment_answers_preferred_answer` CHECK (`preferred_answer` IS NULL OR (`preferred_answer` >= 1 AND `preferred_answer` <= 2)),
    CONSTRAINT `ck_third_party_assessment_answers_weight` CHECK (`weight` >= 1 AND `weight` <= 100),
    CONSTRAINT `fk_third_party_assessment_answers_assessment_id` FOREIGN KEY (`assessment_id`) REFERENCES `third_party_assessments` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `third_party_sbom_components` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `sbom_id` int(11) NOT NULL,
    `name` varchar(300) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `version` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `purl` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `license` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_third_party_sbom_components_sbom_id` FOREIGN KEY (`sbom_id`) REFERENCES `third_party_sboms` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_third_parties_created_by_id` ON `third_parties` (`created_by_id`);

CREATE INDEX IF NOT EXISTS `idx_third_parties_entity_id` ON `third_parties` (`entity_id`);

CREATE INDEX IF NOT EXISTS `idx_third_parties_owner_id` ON `third_parties` (`owner_id`);

CREATE INDEX IF NOT EXISTS `idx_third_parties_status` ON `third_parties` (`status`);

CREATE INDEX IF NOT EXISTS `idx_third_parties_updated_by_id` ON `third_parties` (`updated_by_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_third_parties_name` ON `third_parties` (`name`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_third_party_assessment_answers_assessment_id_question_id` ON `third_party_assessment_answers` (`assessment_id`, `question_id`);

CREATE INDEX IF NOT EXISTS `idx_third_party_assessments_answers_updated_by_id` ON `third_party_assessments` (`answers_updated_by_id`);

CREATE INDEX IF NOT EXISTS `idx_third_party_assessments_created_by_id` ON `third_party_assessments` (`created_by_id`);

CREATE INDEX IF NOT EXISTS `idx_third_party_assessments_third_party_id` ON `third_party_assessments` (`third_party_id`);

CREATE INDEX IF NOT EXISTS `idx_third_party_assessments_voided_by_id` ON `third_party_assessments` (`voided_by_id`);

CREATE INDEX IF NOT EXISTS `idx_third_party_data_locations_created_by_id` ON `third_party_data_locations` (`created_by_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_third_party_data_locations_third_party_id_country_purpose` ON `third_party_data_locations` (`third_party_id`, `country`, `purpose`);

CREATE INDEX IF NOT EXISTS `idx_third_party_links_created_by_id` ON `third_party_links` (`created_by_id`);

CREATE INDEX IF NOT EXISTS `idx_third_party_links_entity_id` ON `third_party_links` (`entity_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_third_party_links_third_party_id_entity_id` ON `third_party_links` (`third_party_id`, `entity_id`);

CREATE INDEX IF NOT EXISTS `idx_third_party_sbom_components_name` ON `third_party_sbom_components` (`name`);

CREATE INDEX IF NOT EXISTS `idx_third_party_sbom_components_sbom_id` ON `third_party_sbom_components` (`sbom_id`);

CREATE INDEX IF NOT EXISTS `idx_third_party_sboms_uploaded_by_id` ON `third_party_sboms` (`uploaded_by_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_third_party_sboms_third_party_id_document_sha256` ON `third_party_sboms` (`third_party_id`, `document_sha256`);

CREATE INDEX IF NOT EXISTS `idx_third_party_subprocessors_created_by_id` ON `third_party_subprocessors` (`created_by_id`);

CREATE INDEX IF NOT EXISTS `idx_third_party_subprocessors_subprocessor_third_party_id` ON `third_party_subprocessors` (`subprocessor_third_party_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_third_party_subprocessors_third_party_id_name` ON `third_party_subprocessors` (`third_party_id`, `name`);
