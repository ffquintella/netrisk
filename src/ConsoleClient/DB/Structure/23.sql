ALTER TABLE `vulnerabilities`
    ADD COLUMN `EntityId` int NULL AFTER `Mskb`,
    ADD CONSTRAINT `fk_vul_ent` FOREIGN KEY (`EntityId`) REFERENCES `netrisk`.`entities` (`Id`) ON DELETE SET NULL ON UPDATE CASCADE;