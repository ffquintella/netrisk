START TRANSACTION;

ALTER TABLE `risks` MODIFY COLUMN `IncidentResponsePlanId` int(11) NULL;

ALTER TABLE `risks` ADD CONSTRAINT `fk_risk_irp` FOREIGN KEY (`IncidentResponsePlanId`) REFERENCES `IncidentResponsePlans` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241204112346_RiskIRPConnection', '8.0.11');

COMMIT;