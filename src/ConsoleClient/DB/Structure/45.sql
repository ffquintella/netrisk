START TRANSACTION;

ALTER TABLE `IncidentResponsePlans` ADD `ApprovedById` int(11) NULL;

CREATE INDEX `IX_IncidentResponsePlans_ApprovedById` ON `IncidentResponsePlans` (`ApprovedById`);

ALTER TABLE `IncidentResponsePlans` ADD CONSTRAINT `fk_irp_approved_by` FOREIGN KEY (`ApprovedById`) REFERENCES `entities` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241209172733_IRPApproval', '8.0.11');

COMMIT;