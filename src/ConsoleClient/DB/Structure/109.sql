-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so this script carries no
-- transaction; every statement is guarded instead, and applying the version again converges on the
-- same schema.

-- M53 security review (S40 §6.4, §7.3, §11.4) -- the evidence the profile approval's segregation of duties reads.
-- EF migration 20261016185505_RiskIndexApprovalEvidence.

-- New columns:
--   risk_index_profiles.submitted_by_id  who submitted the draft, written in the submission's own save
--   nr_files.risk_index_profile_id       the committee's minutes attached to a profile (T262)
-- New tables:
--   permission_grant_events      who granted or revoked a watched permission, moved a user between roles, renamed a
--                                role or edited risk_index_approver_roles; insert-only, outside the audit retention
--   risk_index_profile_editors   who created or edited each draft, written in the same save as the edit

ALTER TABLE `risk_index_profiles` ADD COLUMN IF NOT EXISTS `submitted_by_id` int(11) NULL;

ALTER TABLE `nr_files` ADD COLUMN IF NOT EXISTS `risk_index_profile_id` int(11) NULL;

CREATE TABLE IF NOT EXISTS `permission_grant_events` (
    `id` bigint(20) NOT NULL AUTO_INCREMENT,
    `kind` int(11) NOT NULL,
    `action` int(11) NOT NULL,
    `role_id` int(11) NULL,
    `role_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `user_id` int(11) NULL,
    `permission_key` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `old_value` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `new_value` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `actor_id` int(11) NULL,
    `actor` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `created_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `ck_permission_grant_events_action` CHECK (`action` >= 1 AND `action` <= 2),
    CONSTRAINT `ck_permission_grant_events_kind` CHECK (`kind` >= 1 AND `kind` <= 5),
    CONSTRAINT `fk_permission_grant_events_role_id` FOREIGN KEY (`role_id`) REFERENCES `role` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_permission_grant_events_user_id` FOREIGN KEY (`user_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `risk_index_profile_editors` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `profile_id` int(11) NOT NULL,
    `user_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_risk_index_profile_editors_profile_id` FOREIGN KEY (`profile_id`) REFERENCES `risk_index_profiles` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_risk_index_profile_editors_user_id` FOREIGN KEY (`user_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_risk_index_profiles_submitted_by_id` ON `risk_index_profiles` (`submitted_by_id`);

CREATE INDEX IF NOT EXISTS `idx_nr_files_risk_index_profile_id` ON `nr_files` (`risk_index_profile_id`);

CREATE INDEX IF NOT EXISTS `idx_permission_grant_events_actor_id` ON `permission_grant_events` (`actor_id`);

CREATE INDEX IF NOT EXISTS `idx_permission_grant_events_kind_role_name` ON `permission_grant_events` (`kind`, `role_name`);

CREATE INDEX IF NOT EXISTS `idx_permission_grant_events_role_id_permission_key` ON `permission_grant_events` (`role_id`, `permission_key`);

CREATE INDEX IF NOT EXISTS `idx_permission_grant_events_user_id_kind` ON `permission_grant_events` (`user_id`, `kind`);

CREATE INDEX IF NOT EXISTS `idx_risk_index_profile_editors_user_id` ON `risk_index_profile_editors` (`user_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_risk_index_profile_editors_profile_id_user_id` ON `risk_index_profile_editors` (`profile_id`, `user_id`);

ALTER TABLE `nr_files` ADD CONSTRAINT `fk_nr_files_risk_index_profile_id` FOREIGN KEY IF NOT EXISTS (`risk_index_profile_id`) REFERENCES `risk_index_profiles` (`id`) ON DELETE SET NULL;

ALTER TABLE `risk_index_profiles` ADD CONSTRAINT `fk_risk_index_profiles_submitted_by_id` FOREIGN KEY IF NOT EXISTS (`submitted_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL;
