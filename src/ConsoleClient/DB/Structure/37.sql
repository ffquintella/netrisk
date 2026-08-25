-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenUpdated` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenTested` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenReviewed` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenExercised` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenApproved` tinyint(1) NULL DEFAULT 0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241121143359_fixIncidentResponsePlan2', '8.0.10')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

