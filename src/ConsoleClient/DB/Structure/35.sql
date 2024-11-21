START TRANSACTION;

ALTER TABLE `IncidentResponsePlans` DROP FOREIGN KEY `fk_irp_last_exercised_by`;

ALTER TABLE `IncidentResponsePlans` DROP FOREIGN KEY `fk_irp_last_reviewed_by`;

ALTER TABLE `IncidentResponsePlans` DROP FOREIGN KEY `fk_irp_last_tested_by`;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `LastTestedById` int(11) NULL;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `LastReviewedById` int(11) NULL;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `LastExercisedById` int(11) NULL;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenUpdated` tinyint(1) NOT NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenReviewed` tinyint(1) NOT NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenExercised` tinyint(1) NOT NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenApproved` tinyint(1) NOT NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` ADD CONSTRAINT `fk_irp_last_exercised_by` FOREIGN KEY (`LastExercisedById`) REFERENCES `entities` (`Id`);

ALTER TABLE `IncidentResponsePlans` ADD CONSTRAINT `fk_irp_last_reviewed_by` FOREIGN KEY (`LastReviewedById`) REFERENCES `entities` (`Id`);

ALTER TABLE `IncidentResponsePlans` ADD CONSTRAINT `fk_irp_last_tested_by` FOREIGN KEY (`LastTestedById`) REFERENCES `entities` (`Id`);

COMMIT;