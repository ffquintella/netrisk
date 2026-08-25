-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

ALTER TABLE `IncidentResponsePlans` ADD COLUMN IF NOT EXISTS `ApprovedById` int(11) NULL;

CREATE INDEX IF NOT EXISTS `IX_IncidentResponsePlans_ApprovedById` ON `IncidentResponsePlans` (`ApprovedById`);

ALTER TABLE `IncidentResponsePlans` ADD CONSTRAINT `fk_irp_approved_by` FOREIGN KEY IF NOT EXISTS (`ApprovedById`) REFERENCES `entities` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241209172733_IRPApproval', '8.0.11')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

