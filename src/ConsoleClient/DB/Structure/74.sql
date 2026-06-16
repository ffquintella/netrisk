START TRANSACTION;
-- Schema for the multi-entity scoped roles, redesigned Reports, IRP templates and assessment-run
-- features that shipped in the model (NRDbContext) for 2.11.0 without an accompanying migration.
-- Authentication touches `user_entity_roles` on every login, so its absence broke all auth in prod;
-- the other tables/columns below were drifted in alongside it and are created here in the same step.
ALTER TABLE `risks` ADD `entity_id` int(11) NULL;

ALTER TABLE `incidents` ADD `entity_id` int(11) NULL;

ALTER TABLE `hosts` ADD `entity_id` int(11) NULL;

ALTER TABLE `assessments` ADD `entity_id` int(11) NULL;

ALTER TABLE `assessment_runs` ADD `current_page_index` int(11) NOT NULL DEFAULT '1';

ALTER TABLE `assessment_runs` ADD `progress_percentage` int(11) NOT NULL DEFAULT '0';

ALTER TABLE `assessment_questions` ADD `condition_json` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `assessment_questions` ADD `explanation_markdown` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `assessment_questions` ADD `page_number` int(11) NOT NULL DEFAULT '1';

ALTER TABLE `assessment_questions` ADD `parent_question_id` int(11) NULL;

CREATE TABLE `assessment_run_answers` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `assessment_run_id` int(11) NOT NULL,
    `assessment_question_id` int(11) NOT NULL,
    `answer_content_json` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `last_updated_at` datetime NOT NULL DEFAULT current_timestamp(),
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_assessment_run_answers_question` FOREIGN KEY (`assessment_question_id`) REFERENCES `assessment_questions` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_assessment_run_answers_run` FOREIGN KEY (`assessment_run_id`) REFERENCES `assessment_runs` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `irp_templates` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `description` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `matching_rules_json` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `is_enabled` tinyint(1) NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `report_templates` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `description` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `owner_id` int(11) NOT NULL,
    `created_at` datetime NOT NULL DEFAULT current_timestamp(),
    `updated_at` datetime NOT NULL DEFAULT current_timestamp(),
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_report_templates_owner` FOREIGN KEY (`owner_id`) REFERENCES `user` (`value`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `user_entity_roles` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `user_id` int(11) NOT NULL,
    `entity_id` int(11) NOT NULL,
    `role_id` int(11) NOT NULL,
    `created_at` datetime NOT NULL DEFAULT current_timestamp(),
    `revoked_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_user_entity_roles_entity` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `fk_user_entity_roles_role` FOREIGN KEY (`role_id`) REFERENCES `role` (`value`) ON DELETE CASCADE,
    CONSTRAINT `fk_user_entity_roles_user` FOREIGN KEY (`user_id`) REFERENCES `user` (`value`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `irp_template_tasks` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `irp_template_id` int(11) NOT NULL,
    `title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `instructions_markdown` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `assignee_rule_json` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `due_offset_seconds` int(11) NOT NULL,
    `predecessor_task_id` int(11) NULL,
    `requires_confirmation` tinyint(1) NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_irp_template_tasks_predecessor` FOREIGN KEY (`predecessor_task_id`) REFERENCES `irp_template_tasks` (`id`),
    CONSTRAINT `fk_irp_template_tasks_template` FOREIGN KEY (`irp_template_id`) REFERENCES `irp_templates` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `report_template_versions` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `template_id` int(11) NOT NULL,
    `version` int(11) NOT NULL,
    `layout_json` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `branding_json` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `created_at` datetime NOT NULL DEFAULT current_timestamp(),
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_report_template_versions_template` FOREIGN KEY (`template_id`) REFERENCES `report_templates` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `report_schedules` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `report_template_version_id` int(11) NOT NULL,
    `frequency_cron` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `timezone` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'UTC',
    `recipients_json` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `is_enabled` tinyint(1) NOT NULL,
    `last_run_at` datetime NULL,
    `last_status` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_report_schedules_template_version` FOREIGN KEY (`report_template_version_id`) REFERENCES `report_template_versions` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX `IX_risks_entity_id` ON `risks` (`entity_id`);

CREATE INDEX `IX_incidents_entity_id` ON `incidents` (`entity_id`);

CREATE INDEX `IX_hosts_entity_id` ON `hosts` (`entity_id`);

CREATE INDEX `IX_assessments_entity_id` ON `assessments` (`entity_id`);

CREATE INDEX `fk_assessment_questions_parent` ON `assessment_questions` (`parent_question_id`);

CREATE INDEX `fk_assessment_run_answers_question` ON `assessment_run_answers` (`assessment_question_id`);

CREATE INDEX `fk_assessment_run_answers_run` ON `assessment_run_answers` (`assessment_run_id`);

CREATE INDEX `fk_irp_template_tasks_predecessor` ON `irp_template_tasks` (`predecessor_task_id`);

CREATE INDEX `fk_irp_template_tasks_template` ON `irp_template_tasks` (`irp_template_id`);

CREATE INDEX `fk_report_schedules_template_version` ON `report_schedules` (`report_template_version_id`);

CREATE INDEX `fk_report_template_versions_template` ON `report_template_versions` (`template_id`);

CREATE INDEX `fk_report_templates_owner` ON `report_templates` (`owner_id`);

CREATE INDEX `fk_user_entity_roles_entity` ON `user_entity_roles` (`entity_id`);

CREATE INDEX `fk_user_entity_roles_role` ON `user_entity_roles` (`role_id`);

CREATE INDEX `fk_user_entity_roles_user` ON `user_entity_roles` (`user_id`);

ALTER TABLE `assessment_questions` ADD CONSTRAINT `fk_assessment_questions_parent` FOREIGN KEY (`parent_question_id`) REFERENCES `assessment_questions` (`id`);

ALTER TABLE `assessments` ADD CONSTRAINT `fk_assessments_entity` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL;

ALTER TABLE `hosts` ADD CONSTRAINT `fk_hosts_entity` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL;

ALTER TABLE `incidents` ADD CONSTRAINT `fk_incidents_entity` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL;

ALTER TABLE `risks` ADD CONSTRAINT `fk_risks_entity` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL;

COMMIT;
