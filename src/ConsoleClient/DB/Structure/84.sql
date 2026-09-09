-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- External secret vaults -- the BastionVault integration and the secret-vault plugin capability.
--
--   secret_vault_connections   One (vault, API key) pair, serviced by a secret-vault plugin. The
--                              only credential the feature stores: every other credential column in
--                              the product may now hold a `vault:v1:...` reference through one of
--                              these rows instead of a secret.
--
-- Additive in every sense. One new table, nothing dropped, nothing renamed, and no existing column
-- changes type or nullability -- a reference is a string in a column that already accepts strings.
-- So this version has no destructive gate and needs no observation window.
--
-- Note what is deliberately absent: a join table recording which fields point at which secret.
-- References live in the credential columns themselves (see Model.Secrets.SecretReference for the
-- reasoning), so there is no second place to keep in sync -- and no cascade, which is why deleting a
-- connection is refused while references to it exist rather than silently emptying them.

CREATE TABLE IF NOT EXISTS `secret_vault_connections` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `plugin_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `base_url` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `encrypted_api_key` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `machine_id` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `enabled` tinyint(1) NOT NULL,
    `cache_ttl_minutes` int(11) NOT NULL,
    `last_test_at` datetime NULL,
    `last_test_succeeded` tinyint(1) NULL,
    `last_test_message` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL,
    `updated_at` datetime NULL,
    `created_by_id` int(11) NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_secret_vault_connections_created_by_id` FOREIGN KEY (`created_by_id`) REFERENCES `user` (`value`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IF NOT EXISTS `uq_secret_vault_connections_name` ON `secret_vault_connections` (`name`);

CREATE INDEX IF NOT EXISTS `idx_secret_vault_connections_created_by_id` ON `secret_vault_connections` (`created_by_id`);
