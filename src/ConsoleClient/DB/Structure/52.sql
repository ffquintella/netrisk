START TRANSACTION;
ALTER TABLE `Incidents` RENAME COLUMN `Recomendations` TO `Recommendations`;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241226195742_RecommendationRename', '9.0.0');

COMMIT;