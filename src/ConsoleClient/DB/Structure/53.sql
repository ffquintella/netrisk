START TRANSACTION;
ALTER TABLE `Incidents` ADD `Category` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT '';

ALTER TABLE `Incidents` ADD `ImpactedEntityId` int(11) NULL;

CREATE INDEX `idx_category` ON `Incidents` (`Category`);

CREATE INDEX `IX_Incidents_ImpactedEntityId` ON `Incidents` (`ImpactedEntityId`);

ALTER TABLE `Incidents` ADD CONSTRAINT `fk_inc_impacted_entity` FOREIGN KEY (`ImpactedEntityId`) REFERENCES `entities` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241229213258_NewIncidentFields2', '9.0.0');

COMMIT;