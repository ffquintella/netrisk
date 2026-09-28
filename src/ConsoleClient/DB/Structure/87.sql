-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Secret vault connections: the values of the controls a plugin contributes to the connection
-- editor.
--
--   secret_vault_connections.extra_settings  A JSON object of string to string, keyed by the
--                                            plugin's own field keys -- a namespace, a mount, a
--                                            tenant: whatever the vault needs that base_url,
--                                            machine_id and app_id do not name. NULL for every
--                                            existing row and for every plugin that declares
--                                            nothing.
--
-- One column and not one per field on purpose: the whole point of plugin-contributed screens is
-- that a plugin adds a control without this table changing. A column per field would put every
-- vault's vocabulary into NetRisk's schema and make the next one a migration.
--
-- Not encrypted, and not allowed to hold a credential -- same rule as machine_id and app_id, which
-- name the caller rather than authenticate it. The API key remains the only credential this table
-- holds.
--
-- One nullable column, nothing dropped or renamed, so this version has no destructive gate and
-- needs no observation window.

ALTER TABLE `secret_vault_connections`
    ADD COLUMN IF NOT EXISTS `extra_settings` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;
