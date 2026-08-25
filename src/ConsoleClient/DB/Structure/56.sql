-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

ALTER TABLE `user` DROP COLUMN IF EXISTS `username`;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20250106132304_RemoveUsername', '9.0.0')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

