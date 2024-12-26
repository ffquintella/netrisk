START TRANSACTION;
ALTER TABLE `Incidents` ADD `AssignedToId` int(11) NULL;

ALTER TABLE `Incidents` ADD `Recomendations` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL;

ALTER TABLE `Incidents` ADD `ReportDate` datetime NOT NULL DEFAULT current_timestamp();

ALTER TABLE `Incidents` ADD `ReportEntityId` int(11) NULL;

ALTER TABLE `Incidents` ADD `ReportedBy` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL;

ALTER TABLE `Incidents` ADD `ReportedByEntity` tinyint(1) NOT NULL DEFAULT 0;

CREATE FULLTEXT INDEX `idx_reported_by` ON `Incidents` (`ReportedBy`);

CREATE INDEX `IX_Incidents_AssignedToId` ON `Incidents` (`AssignedToId`);

CREATE INDEX `IX_Incidents_ReportEntityId` ON `Incidents` (`ReportEntityId`);

ALTER TABLE `Incidents` ADD CONSTRAINT `fk_inc_report_entity` FOREIGN KEY (`ReportEntityId`) REFERENCES `entities` (`Id`);

ALTER TABLE `Incidents` ADD CONSTRAINT `fk_inc_report_user` FOREIGN KEY (`AssignedToId`) REFERENCES `user` (`value`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241226143834_NewIncidentFields', '9.0.0');

COMMIT;