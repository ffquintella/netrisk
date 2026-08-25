-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

ALTER TABLE `risks` MODIFY COLUMN `IncidentResponsePlanId` int(11) NULL;

ALTER TABLE `risks` ADD CONSTRAINT `fk_risk_irp` FOREIGN KEY IF NOT EXISTS (`IncidentResponsePlanId`) REFERENCES `IncidentResponsePlans` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241204112346_RiskIRPConnection', '8.0.11')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

