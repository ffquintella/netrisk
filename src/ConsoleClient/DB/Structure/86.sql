-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Secret vault connections: application identity and a per-connection TLS escape hatch.
--
--   secret_vault_connections.app_id             The application identity the vault authorizes this
--                                               installation as (BastionVault's app_id). Nullable:
--                                               a vault that authorizes on the key alone has none,
--                                               and the plugin declares whether it is required.
--   secret_vault_connections.ignore_ssl_errors  Whether calls to this vault skip TLS certificate
--                                               validation. Defaults to 0, and every existing row
--                                               reads back 0 -- an upgrade must not switch a
--                                               security control off for a connection nobody
--                                               touched.
--
-- Two nullable/defaulted columns on one table, nothing dropped or renamed, so this version has no
-- destructive gate and needs no observation window.
--
-- app_id is not encrypted: like machine_id it names the caller rather than authenticating it, and an
-- operator has to be able to read it back to compare it against the vault's policies.

ALTER TABLE `secret_vault_connections`
    ADD COLUMN IF NOT EXISTS `app_id` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;

ALTER TABLE `secret_vault_connections`
    ADD COLUMN IF NOT EXISTS `ignore_ssl_errors` tinyint(1) NOT NULL DEFAULT 0;
