START TRANSACTION;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenUpdated` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenTested` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenReviewed` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenExercised` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenApproved` tinyint(1) NULL DEFAULT 0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241121143359_fixIncidentResponsePlan2', '8.0.10');

COMMIT;