START TRANSACTION;
ALTER TABLE `user` ADD `login` VARCHAR(250) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT '';

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20250106124833_LoginProperty', '9.0.0');

COMMIT;