DROP TABLE IF EXISTS comments;

CREATE TABLE `comments`  (
                                       `id` int NOT NULL AUTO_INCREMENT,
                                       `UserId` int NULL,
                                       `IsAnonymous` tinyint NOT NULL DEFAULT 0,
                                       `CommenterName` varchar(255) NULL,
                                       `Date` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                       `ReplyTo` int NULL,
                                       `Type` varchar(255) NULL,
                                       `Text` text NULL,
                                       `FixRequestId` int NULL,
                                       `RiskId` int NULL,
                                       `VulnerabilityId` int NULL,
                                       `HostId` int NULL,
                                       PRIMARY KEY (`id`),
                                       INDEX `idx-commenterName`(`CommenterName`) USING BTREE,
                                       INDEX `idx-type-comments`(`Type`) USING BTREE,
                                       FULLTEXT INDEX `idx-full-text`(`Text`),
                                       CONSTRAINT `fk_user_id_comments` FOREIGN KEY (`UserId`) REFERENCES `user` (`value`) ON DELETE CASCADE ON UPDATE CASCADE,
                                       CONSTRAINT `fk_fix_request_comments` FOREIGN KEY (`FixRequestId`) REFERENCES `FixRequest` (`Id`) ON DELETE CASCADE ON UPDATE CASCADE,
                                       CONSTRAINT `fk_risk_id_comments` FOREIGN KEY (`RiskId`) REFERENCES `risks` (`id`) ON DELETE CASCADE ON UPDATE CASCADE,
                                       CONSTRAINT `fk_vulnerability_id_comments` FOREIGN KEY (`VulnerabilityId`) REFERENCES `vulnerabilities` (`Id`) ON DELETE CASCADE ON UPDATE CASCADE,
                                       CONSTRAINT `fk_host_id_comments` FOREIGN KEY (`HostId`) REFERENCES `hosts` (`Id`) ON DELETE CASCADE ON UPDATE CASCADE
);