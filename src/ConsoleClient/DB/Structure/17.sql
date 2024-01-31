
ALTER TABLE `assessment_runs`
    ADD COLUMN `HostId` int NULL AFTER `Comments`,
ADD CONSTRAINT `fkHost` FOREIGN KEY (`HostId`) REFERENCES `hosts` (`Id`) ON DELETE SET NULL;