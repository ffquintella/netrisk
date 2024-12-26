START TRANSACTION;
ALTER TABLE `Incidents` RENAME COLUMN `Resolution` TO `Solution`;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241226192829_SolutionRename', '9.0.0');

COMMIT;