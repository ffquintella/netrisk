CREATE TABLE `entities`  (
                             `Id` int NOT NULL AUTO_INCREMENT,
                             `DefinitionName` varchar(255) NOT NULL,
                             `DefinitionVersion` varchar(15) NOT NULL,
                             `Created` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
                             `Updated` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                             `CreatedBy` int NOT NULL,
                             `UpdatedBy` int NOT NULL,
                             `Status` varchar(15) NOT NULL,
                             PRIMARY KEY (`Id`),
                             INDEX `idx_definition_name`(`DefinitionName`) USING BTREE
);

CREATE TABLE `entities_properties`  (
                            `Id` int NOT NULL,
                            `Type` varchar(255) NOT NULL,
                            `Value` text NOT NULL,
                            `OldValue` text NOT NULL,
                            `Entity` int NOT NULL,
                            `Name` varchar(255) NOT NULL,
                            PRIMARY KEY (`Id`),
                            FULLTEXT INDEX `idx_value`(`Value`),
                            INDEX `idx_name`(`Name`) USING BTREE,
                            CONSTRAINT `fk_entity` FOREIGN KEY (`Entity`) REFERENCES `entities` (`Id`) ON DELETE CASCADE ON UPDATE NO ACTION
    );