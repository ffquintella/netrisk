-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema -- that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- The schema the security findings Track 7 deferred to Track 8 needed.
--
--   revoked_tokens    NR-2026-028: per-jti revocation, so "sign out this one session" can actually
--                     invalidate the token rather than only forgetting it client-side. Rows are
--                     pruned past expires_at, so the table is bounded by the token lifetime.
--   login_attempts    NR-2026-008b: the brute-force counter, shared across API instances. The
--                     in-memory dictionary it replaces gave every instance its own budget, so an
--                     attacker spreading attempts multiplied the allowance by the instance count.
--   nr_files.entity_id  NR-2026-017: brings attachments under the Track 2.3 query filters, which is
--                     what closes the cross-tenant read. Nullable, and a null stays visible --
--                     legacy rows predate the column and hiding every existing attachment from
--                     every scoped user would be a regression, not a fix.

ALTER TABLE `nr_files` ADD COLUMN IF NOT EXISTS `entity_id` int(11) NULL;

CREATE TABLE IF NOT EXISTS `login_attempts` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `identity` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `source` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `failure_count` int(11) NOT NULL,
    `first_failure_at` datetime NOT NULL,
    `last_failure_at` datetime NOT NULL,
    `locked_until` datetime NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `revoked_tokens` (
    `id` int(11) NOT NULL AUTO_INCREMENT,
    `jti` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `user_id` int(11) NULL,
    `revoked_at` datetime NOT NULL,
    `expires_at` datetime NOT NULL,
    `reason` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `fk_revoked_tokens_user_id` FOREIGN KEY (`user_id`) REFERENCES `user` (`value`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS `idx_nr_files_entity_id` ON `nr_files` (`entity_id`);

CREATE INDEX IF NOT EXISTS `idx_login_attempts_last_failure_at` ON `login_attempts` (`last_failure_at`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_login_attempts_identity_source` ON `login_attempts` (`identity`, `source`);

CREATE INDEX IF NOT EXISTS `idx_revoked_tokens_expires_at` ON `revoked_tokens` (`expires_at`);

CREATE INDEX IF NOT EXISTS `IX_revoked_tokens_user_id` ON `revoked_tokens` (`user_id`);

CREATE UNIQUE INDEX IF NOT EXISTS `uq_revoked_tokens_jti` ON `revoked_tokens` (`jti`);

ALTER TABLE `nr_files`
    ADD CONSTRAINT `fk_nr_files_entity_id` FOREIGN KEY IF NOT EXISTS (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE SET NULL;
