CREATE TABLE IF NOT EXISTS `actions`  (
                                       `Id` int NOT NULL AUTO_INCREMENT,
                                       `ObjectType` varchar(255) NOT NULL,
                                       `DateTime` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                       `UserId` int,
                                       `Message` varchar(255) NULL,
                                       PRIMARY KEY (`Id`),
                                       FULLTEXT INDEX `idx_object_type`(`ObjectType`),
                                       CONSTRAINT `fx_action_user` FOREIGN KEY (`UserId`) REFERENCES `netrisk`.`user` (`value`) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS `vulnerabilities_to_actions`  (
                                       `vulnerabilityId` int NOT NULL,
                                       `actionId` int NOT NULL,
                                       PRIMARY KEY (`vulnerabilityId`, `actionId`),
                                       CONSTRAINT `fk_vul_act_1` FOREIGN KEY (`vulnerabilityId`) REFERENCES `netrisk`.`vulnerabilities` (`Id`) ON DELETE CASCADE,
                                       CONSTRAINT `fk_vul_act2` FOREIGN KEY (`actionId`) REFERENCES `netrisk`.`actions` (`Id`) ON DELETE CASCADE
);