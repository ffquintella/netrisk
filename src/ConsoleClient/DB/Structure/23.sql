ALTER TABLE `vulnerabilities`
    ADD COLUMN IF NOT EXISTS `EntityId` int NULL AFTER `Mskb`,
    ADD CONSTRAINT `fk_vul_ent` FOREIGN KEY IF NOT EXISTS (`EntityId`) REFERENCES `netrisk`.`entities` (`Id`) ON DELETE SET NULL ON UPDATE CASCADE;