-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 4 (Integrations & Notification Channels).
--
-- Purely additive: fifteen new tables, four new columns on `hosts`, four on `entities`, and nothing
-- existing is dropped, renamed or retyped. An installation that never configures an integration
-- carries fifteen empty tables and behaves exactly as before.
--
-- The groups, in the order the foreign keys require them:
--   4.1  notification_channels → notification_subscriptions → notification_deliveries
--   4.2  issue_tracker_connections → issue_status_mappings, finding_issue_links
--   4.3  identity_providers → scim_tokens → scim_request_logs; webauthn_credentials, mfa_recovery_codes
--   4.4  trendmicro_connections; hosts.external_id/os_version/criticality/risk_score
--   4.5  securityscorecard_connections → security_scorecard_factors; entities.cyber_risk_index
--   —    integration_sync_logs, shared by every integration's sync job
--
-- Every table follows the Track 6 convention (snake_case plural names, `fk_`/`idx_`/`uq_` prefixes,
-- int-backed enums, tinyint(1) booleans, UTC datetimes, varchar/text and never BLOB for text)
-- because new schema is expected to be born compliant rather than added to the drift.
--
-- Credential columns are named `encrypted_*` and hold ciphertext, never a plaintext token: a Slack
-- webhook URL or a Jira PAT in a database dump is a working credential, and the whole point of
-- putting them behind the settings infrastructure is that a dump is not.

ALTER TABLE `hosts` ADD COLUMN IF NOT EXISTS `criticality` int(11) NULL;

ALTER TABLE `hosts` ADD COLUMN IF NOT EXISTS `external_id` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `hosts` ADD COLUMN IF NOT EXISTS `external_provider` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `hosts` ADD COLUMN IF NOT EXISTS `os_version` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `hosts` ADD COLUMN IF NOT EXISTS `risk_score` int(11) NULL;

ALTER TABLE `hosts` ADD COLUMN IF NOT EXISTS `risk_score_source` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `hosts` ADD COLUMN IF NOT EXISTS `risk_score_updated_at` datetime NULL;

ALTER TABLE `entities` ADD COLUMN IF NOT EXISTS `cyber_risk_index` double NULL;

ALTER TABLE `entities` ADD COLUMN IF NOT EXISTS `posture_grade` varchar(8) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `entities` ADD COLUMN IF NOT EXISTS `posture_source` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `entities` ADD COLUMN IF NOT EXISTS `posture_updated_at` datetime NULL;

CREATE TABLE IF NOT EXISTS `identity_providers` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `protocol` int(11) NOT NULL,
    `enabled` tinyint(1) NOT NULL,
    `authority` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `client_id` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `encrypted_client_secret` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `scopes` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `metadata_url` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `metadata_xml` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `sp_entity_id` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `acs_url` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `require_signed_assertions` tinyint(1) NOT NULL,
    `clock_skew_seconds` int(11) NOT NULL,
    `supports_single_logout` tinyint(1) NOT NULL,
    `claim_mapping_json` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `group_mapping_json` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `jit_provisioning` tinyint(1) NOT NULL,
    `default_role_id` int(11) NULL,
    `default_entity_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_identity_providers_default_entity_id` FOREIGN KEY (`default_entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `fk_identity_providers_default_role_id` FOREIGN KEY (`default_role_id`) REFERENCES `role` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `integration_sync_logs` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `integration` int(11) NOT NULL,
    `connection_id` int(11) NULL,
    `connection_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `started_at` datetime NOT NULL,
    `finished_at` datetime NULL,
    `status` int(11) NOT NULL,
    `created_count` int(11) NOT NULL,
    `updated_count` int(11) NOT NULL,
    `skipped_count` int(11) NOT NULL,
    `failed_count` int(11) NOT NULL,
    `summary` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `error_message` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `issue_tracker_connections` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `provider` int(11) NOT NULL,
    `base_url` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `project_key` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `issue_type` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `auth_user` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `encrypted_token` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `encrypted_webhook_secret` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `priority_mapping_json` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `title_template` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `description_template` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `default_labels` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `entity_id` int(11) NULL,
    `enabled` tinyint(1) NOT NULL,
    `auto_create_min_severity` int(11) NULL,
    `push_finding_updates` tinyint(1) NOT NULL,
    `poll_interval_minutes` int(11) NOT NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_issue_tracker_connections_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_issue_tracker_connections_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `mfa_recovery_codes` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `user_id` int(11) NOT NULL,
    `code_hash` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    `used_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_mfa_recovery_codes_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_mfa_recovery_codes_user_id` FOREIGN KEY (`user_id`) REFERENCES `user` (`value`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `notification_channels` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `kind` int(11) NOT NULL,
    `configuration_json` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `secrets_encrypted` tinyint(1) NOT NULL,
    `enabled` tinyint(1) NOT NULL,
    `fallback_channel_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_notification_channels_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_notification_channels_fallback_channel_id` FOREIGN KEY (`fallback_channel_id`) REFERENCES `notification_channels` (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `securityscorecard_connections` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `domain` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `base_url` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `encrypted_api_token` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `entity_id` int(11) NULL,
    `enabled` tinyint(1) NOT NULL,
    `sync_interval_hours` int(11) NOT NULL,
    `sync_vulnerabilities` tinyint(1) NOT NULL,
    `sync_issues` tinyint(1) NOT NULL,
    `last_sync_at` datetime NULL,
    `last_sync_status` int(11) NULL,
    `last_sync_error` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_securityscorecard_connections_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `trendmicro_connections` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `region` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `base_url` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `encrypted_api_key` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `entity_id` int(11) NULL,
    `enabled` tinyint(1) NOT NULL,
    `sync_interval_hours` int(11) NOT NULL,
    `sync_vulnerabilities` tinyint(1) NOT NULL,
    `sync_risk_scores` tinyint(1) NOT NULL,
    `virtual_patch_closes_finding` tinyint(1) NOT NULL,
    `push_exemptions` tinyint(1) NOT NULL,
    `last_sync_at` datetime NULL,
    `last_sync_status` int(11) NULL,
    `last_sync_error` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_trendmicro_connections_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `webauthn_credentials` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `user_id` int(11) NOT NULL,
    `credential_id` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `public_key` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `sign_count` bigint(20) NOT NULL,
    `aaguid` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `attestation_format` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `is_backup_eligible` tinyint(1) NOT NULL,
    `is_backed_up` tinyint(1) NOT NULL,
    `created_at` datetime NOT NULL,
    `last_used_at` datetime NULL,
    `revoked_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_webauthn_credentials_user_id` FOREIGN KEY (`user_id`) REFERENCES `user` (`value`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `scim_tokens` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `key_id` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `secret_hash` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `identity_provider_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    `last_used_at` datetime NULL,
    `revoked_at` datetime NULL,
    `revoked_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_scim_tokens_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_scim_tokens_identity_provider_id` FOREIGN KEY (`identity_provider_id`) REFERENCES `identity_providers` (`id`) ON DELETE SET NULL,
    CONSTRAINT `fk_scim_tokens_revoked_by_id` FOREIGN KEY (`revoked_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `finding_issue_links` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `vulnerability_id` int(11) NOT NULL,
    `connection_id` int(11) NOT NULL,
    `issue_key` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `issue_id` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `issue_url` varchar(1024) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `last_synced_status` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `last_sync_at` datetime NULL,
    `sync_error` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `last_change_from_remote` tinyint(1) NOT NULL,
    `has_conflict` tinyint(1) NOT NULL,
    `conflict_detail` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_finding_issue_links_connection_id` FOREIGN KEY (`connection_id`) REFERENCES `issue_tracker_connections` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_finding_issue_links_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL,
    CONSTRAINT `fk_finding_issue_links_vulnerability_id` FOREIGN KEY (`vulnerability_id`) REFERENCES `vulnerabilities` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `issue_status_mappings` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `connection_id` int(11) NOT NULL,
    `external_status` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `action` int(11) NOT NULL,
    `outbound_transition` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_issue_status_mappings_connection_id` FOREIGN KEY (`connection_id`) REFERENCES `issue_tracker_connections` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `notification_subscriptions` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `event_type` int(11) NOT NULL,
    `channel_id` int(11) NOT NULL,
    `min_severity` int(11) NULL,
    `entity_id` int(11) NULL,
    `enabled` tinyint(1) NOT NULL,
    `digest_window_minutes` int(11) NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_notification_subscriptions_channel_id` FOREIGN KEY (`channel_id`) REFERENCES `notification_channels` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_notification_subscriptions_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `security_scorecard_factors` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `connection_id` int(11) NOT NULL,
    `entity_id` int(11) NULL,
    `factor_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `score` int(11) NOT NULL,
    `grade` varchar(8) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `issue_count` int(11) NULL,
    `is_overall` tinyint(1) NOT NULL,
    `captured_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_ssc_factors_connection_id` FOREIGN KEY (`connection_id`) REFERENCES `securityscorecard_connections` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_ssc_factors_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `scim_request_logs` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `token_id` int(11) NULL,
    `method` varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `path` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `status_code` int(11) NOT NULL,
    `target` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `outcome` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `occurred_at` datetime NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_scim_request_logs_token_id` FOREIGN KEY (`token_id`) REFERENCES `scim_tokens` (`id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `notification_deliveries` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `subscription_id` int(11) NULL,
    `channel_id` int(11) NULL,
    `event_type` int(11) NOT NULL,
    `status` int(11) NOT NULL,
    `attempts` int(11) NOT NULL,
    `title` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `payload_json` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `last_error` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `severity` int(11) NULL,
    `subject_type` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `subject_id` int(11) NULL,
    `created_at` datetime NOT NULL,
    `last_attempt_at` datetime NULL,
    `delivered_at` datetime NULL,
    `digest_due_at` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_notification_deliveries_channel_id` FOREIGN KEY (`channel_id`) REFERENCES `notification_channels` (`id`) ON DELETE SET NULL,
    CONSTRAINT `fk_notification_deliveries_subscription_id` FOREIGN KEY (`subscription_id`) REFERENCES `notification_subscriptions` (`id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_hosts_external_provider_id` ON `hosts` (`external_provider`, `external_id`);

CREATE INDEX IF NOT EXISTS `idx_hosts_risk_score` ON `hosts` (`risk_score`);

CREATE INDEX IF NOT EXISTS `idx_finding_issue_links_created_by_id` ON `finding_issue_links` (`created_by_id`);

CREATE INDEX IF NOT EXISTS `idx_finding_issue_links_has_conflict` ON `finding_issue_links` (`has_conflict`);

CREATE INDEX IF NOT EXISTS `idx_finding_issue_links_vulnerability_id` ON `finding_issue_links` (`vulnerability_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_finding_issue_links_connection_issue` ON `finding_issue_links` (`connection_id`, `issue_key`);

CREATE INDEX IF NOT EXISTS `idx_identity_providers_default_entity_id` ON `identity_providers` (`default_entity_id`);

CREATE INDEX IF NOT EXISTS `idx_identity_providers_default_role_id` ON `identity_providers` (`default_role_id`);

CREATE INDEX IF NOT EXISTS `idx_identity_providers_protocol_enabled` ON `identity_providers` (`protocol`, `enabled`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_identity_providers_name` ON `identity_providers` (`name`);

CREATE INDEX IF NOT EXISTS `idx_integration_sync_logs_integration_started` ON `integration_sync_logs` (`integration`, `started_at`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_issue_status_mappings_connection_status` ON `issue_status_mappings` (`connection_id`, `external_status`);

CREATE INDEX IF NOT EXISTS `idx_issue_tracker_connections_created_by_id` ON `issue_tracker_connections` (`created_by_id`);

CREATE INDEX IF NOT EXISTS `idx_issue_tracker_connections_entity_id` ON `issue_tracker_connections` (`entity_id`);

CREATE INDEX IF NOT EXISTS `idx_issue_tracker_connections_provider_enabled` ON `issue_tracker_connections` (`provider`, `enabled`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_issue_tracker_connections_name` ON `issue_tracker_connections` (`name`);

CREATE INDEX IF NOT EXISTS `idx_mfa_recovery_codes_created_by_id` ON `mfa_recovery_codes` (`created_by_id`);

CREATE INDEX IF NOT EXISTS `idx_mfa_recovery_codes_user_used` ON `mfa_recovery_codes` (`user_id`, `used_at`);

CREATE INDEX IF NOT EXISTS `idx_notification_channels_created_by_id` ON `notification_channels` (`created_by_id`);

CREATE INDEX IF NOT EXISTS `idx_notification_channels_fallback_channel_id` ON `notification_channels` (`fallback_channel_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_notification_channels_name` ON `notification_channels` (`name`);

CREATE INDEX IF NOT EXISTS `idx_notification_deliveries_channel_id` ON `notification_deliveries` (`channel_id`);

CREATE INDEX IF NOT EXISTS `idx_notification_deliveries_status_created_at` ON `notification_deliveries` (`status`, `created_at`);

CREATE INDEX IF NOT EXISTS `idx_notification_deliveries_subscription_id` ON `notification_deliveries` (`subscription_id`);

CREATE INDEX IF NOT EXISTS `idx_notification_subscriptions_channel_id` ON `notification_subscriptions` (`channel_id`);

CREATE INDEX IF NOT EXISTS `idx_notification_subscriptions_entity_id` ON `notification_subscriptions` (`entity_id`);

CREATE INDEX IF NOT EXISTS `idx_notification_subscriptions_event_enabled` ON `notification_subscriptions` (`event_type`, `enabled`);

CREATE INDEX IF NOT EXISTS `idx_scim_request_logs_occurred_at` ON `scim_request_logs` (`occurred_at`);

CREATE INDEX IF NOT EXISTS `idx_scim_request_logs_token_id` ON `scim_request_logs` (`token_id`);

CREATE INDEX IF NOT EXISTS `idx_scim_tokens_created_by_id` ON `scim_tokens` (`created_by_id`);

CREATE INDEX IF NOT EXISTS `idx_scim_tokens_identity_provider_id` ON `scim_tokens` (`identity_provider_id`);

CREATE INDEX IF NOT EXISTS `idx_scim_tokens_revoked_by_id` ON `scim_tokens` (`revoked_by_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_scim_tokens_key_id` ON `scim_tokens` (`key_id`);

CREATE INDEX IF NOT EXISTS `idx_ssc_factors_connection_factor_captured` ON `security_scorecard_factors` (`connection_id`, `factor_name`, `captured_at`);

CREATE INDEX IF NOT EXISTS `idx_ssc_factors_entity_id` ON `security_scorecard_factors` (`entity_id`);

CREATE INDEX IF NOT EXISTS `idx_securityscorecard_connections_entity_id` ON `securityscorecard_connections` (`entity_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_securityscorecard_connections_name` ON `securityscorecard_connections` (`name`);

CREATE INDEX IF NOT EXISTS `idx_trendmicro_connections_entity_id` ON `trendmicro_connections` (`entity_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_trendmicro_connections_name` ON `trendmicro_connections` (`name`);

CREATE INDEX IF NOT EXISTS `idx_webauthn_credentials_user_id` ON `webauthn_credentials` (`user_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_webauthn_credentials_credential_id` ON `webauthn_credentials` (`credential_id`);
