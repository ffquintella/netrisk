﻿CREATE TABLE `FixRequest`  (
                                       `Id` int NOT NULL AUTO_INCREMENT,
                                       `VulnerabilityId` int NOT NULL,
                                       `Identifier` varchar(255) NOT NULL,
                                       `CreationDate` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                       `LastInteraction` datetime NULL ON UPDATE CURRENT_TIMESTAMP,
                                       `Comments` text NULL,
                                       `Status` int NOT NULL DEFAULT 1,
                                       `FixTeamId` int NULL,
                                       `IsTeamFix` bool NULL,
                                       `SingleFixDestination` varchar(255) NULL,
                                       `TargetDate` datetime NULL,
                                       `FixDate` datetime NULL,
                                       `LastReportingUserId` int NULL,
                                       `RequestingUserId` int NULL,
                                       PRIMARY KEY (`Id`),
                                       UNIQUE INDEX `idx_identifier`(`Identifier`) USING BTREE,
                                       CONSTRAINT `fk_vulnerability` FOREIGN KEY (`VulnerabilityId`) REFERENCES `vulnerabilities` (`Id`) ON DELETE CASCADE ON UPDATE CASCADE,
                                       CONSTRAINT `fk_fixteam` FOREIGN KEY (`FixTeamId`) REFERENCES `team` (`value`) ON DELETE SET NULL ON UPDATE CASCADE,
                                       CONSTRAINT `fk_lastReportingUser` FOREIGN KEY (`LastReportingUserId`) REFERENCES `user` (`value`) ON DELETE SET NULL ON UPDATE CASCADE,
                                       CONSTRAINT `fk_requesting_user_id` FOREIGN KEY (`RequestingUserId`) REFERENCES `netrisk`.`user` (`value`) ON DELETE RESTRICT ON UPDATE CASCADE
);


