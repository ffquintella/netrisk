START TRANSACTION;

ALTER TABLE `nr_files` ADD `IncidentId` int NULL;

ALTER TABLE `nr_actions` ADD `IncidentId` int NULL;

CREATE TABLE `Incidents` (
                             `Id` int NOT NULL AUTO_INCREMENT,
                             `Year` int NOT NULL,
                             `Sequence` int NOT NULL,
                             `Name` varchar(250) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                             `Description` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                             `CreationDate` datetime NOT NULL DEFAULT current_timestamp(),
                             `CreatedById` int(11) NOT NULL,
                             `LastUpdate` datetime NOT NULL DEFAULT current_timestamp(),
                             `UpdatedById` int(11) NULL,
                             `Status` int(6) NOT NULL,
                             `Report` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL,
                             `Notes` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL,
                             `Impact` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL,
                             `Cause` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL,
                             `Resolution` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL,
                             `Duration` bigint NULL,
                             `StartDate` datetime NULL DEFAULT current_timestamp(),
                             CONSTRAINT `PRIMARY` PRIMARY KEY (`Id`),
                             CONSTRAINT `fk_inc_created_by` FOREIGN KEY (`CreatedById`) REFERENCES `user` (`value`) ON DELETE CASCADE,
                             CONSTRAINT `fk_inc_updated_by` FOREIGN KEY (`UpdatedById`) REFERENCES `user` (`value`)
) CHARACTER SET=utf8mb3 COLLATE=utf8mb3_general_ci;

CREATE TABLE `IncidentToIncidentResponsePlan` (
                                                  `IncidentId` int NOT NULL,
                                                  `IncidentResponsePlanId` int NOT NULL,
                                                  CONSTRAINT `PK_IncidentToIncidentResponsePlan` PRIMARY KEY (`IncidentId`, `IncidentResponsePlanId`),
                                                  CONSTRAINT `FK_IncidentToIncidentResponsePlan_IncidentResponsePlans_Inciden~` FOREIGN KEY (`IncidentResponsePlanId`) REFERENCES `IncidentResponsePlans` (`Id`) ON DELETE CASCADE,
                                                  CONSTRAINT `FK_IncidentToIncidentResponsePlan_Incidents_IncidentId` FOREIGN KEY (`IncidentId`) REFERENCES `Incidents` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE INDEX `IX_nr_files_IncidentId` ON `nr_files` (`IncidentId`);

CREATE INDEX `IX_nr_actions_IncidentId` ON `nr_actions` (`IncidentId`);

CREATE FULLTEXT INDEX `idx_inc_name` ON `Incidents` (`Name`);

CREATE FULLTEXT INDEX `idx_inc_repo` ON `Incidents` (`Name`);

CREATE INDEX `IX_Incidents_CreatedById` ON `Incidents` (`CreatedById`);

CREATE INDEX `IX_Incidents_UpdatedById` ON `Incidents` (`UpdatedById`);

CREATE INDEX `IX_IncidentToIncidentResponsePlan_IncidentResponsePlanId` ON `IncidentToIncidentResponsePlan` (`IncidentResponsePlanId`);

ALTER TABLE `nr_actions` ADD CONSTRAINT `fk_inc_actions` FOREIGN KEY (`IncidentId`) REFERENCES `Incidents` (`Id`);

ALTER TABLE `nr_files` ADD CONSTRAINT `fk_inc_attachments` FOREIGN KEY (`IncidentId`) REFERENCES `Incidents` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241125183616_IncidentEntity', '8.0.10');

COMMIT;