START TRANSACTION;
ALTER TABLE `user` DROP COLUMN `username`;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20250106132304_RemoveUsername', '9.0.0');

COMMIT;