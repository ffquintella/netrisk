-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

ALTER TABLE `IncidentResponsePlans` DROP FOREIGN KEY IF EXISTS `fk_irp_last_exercised_by`;

ALTER TABLE `IncidentResponsePlans` DROP FOREIGN KEY IF EXISTS `fk_irp_last_reviewed_by`;

ALTER TABLE `IncidentResponsePlans` DROP FOREIGN KEY IF EXISTS `fk_irp_last_tested_by`;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `LastTestedById` int(11) NULL;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `LastReviewedById` int(11) NULL;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `LastExercisedById` int(11) NULL;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenUpdated` tinyint(1) NOT NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenReviewed` tinyint(1) NOT NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenExercised` tinyint(1) NOT NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenApproved` tinyint(1) NOT NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` ADD CONSTRAINT `fk_irp_last_exercised_by` FOREIGN KEY IF NOT EXISTS (`LastExercisedById`) REFERENCES `entities` (`Id`);

ALTER TABLE `IncidentResponsePlans` ADD CONSTRAINT `fk_irp_last_reviewed_by` FOREIGN KEY IF NOT EXISTS (`LastReviewedById`) REFERENCES `entities` (`Id`);

ALTER TABLE `IncidentResponsePlans` ADD CONSTRAINT `fk_irp_last_tested_by` FOREIGN KEY IF NOT EXISTS (`LastTestedById`) REFERENCES `entities` (`Id`);

