-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

ALTER TABLE `IncidentResponsePlanTasks` DROP FOREIGN KEY IF EXISTS `FK_IncidentResponsePlanTasks_user_CreatedByValue`;

ALTER TABLE `IncidentResponsePlanTasks` DROP FOREIGN KEY IF EXISTS `FK_IncidentResponsePlanTasks_user_UpdatedByValue`;

DROP TABLE IF EXISTS `IncidentResponsePlanTaskToEntity`;

ALTER TABLE `IncidentResponsePlanTasks` DROP INDEX IF EXISTS `IX_IncidentResponsePlanTasks_CreatedByValue`;

ALTER TABLE `IncidentResponsePlanTasks` DROP COLUMN IF EXISTS `CreatedByValue`;

-- Guarded so re-running this version converges: the column is already named `UpdatedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.COLUMNS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'IncidentResponsePlanTasks' AND BINARY COLUMN_NAME = 'UpdatedById') > 0,
                 'DO 0', 'ALTER TABLE `IncidentResponsePlanTasks` RENAME COLUMN `UpdatedByValue` TO `UpdatedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_IncidentResponsePlanTasks_UpdatedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'IncidentResponsePlanTasks' AND BINARY INDEX_NAME = 'IX_IncidentResponsePlanTasks_UpdatedById') > 0,
                 'DO 0', 'ALTER TABLE `IncidentResponsePlanTasks` RENAME INDEX `IX_IncidentResponsePlanTasks_UpdatedByValue` TO `IX_IncidentResponsePlanTasks_UpdatedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `Status` int(11) NOT NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `Priority` int(11) NOT NULL DEFAULT 1;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `PlanId` int(11) NOT NULL;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `Notes` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `LastUpdate` datetime(6) NULL;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `LastTestDate` datetime NULL;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `IsSequential` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `IsParallel` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `IsOptional` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `HasBeenTested` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `ExecutionOrder` int(11) NOT NULL DEFAULT 1;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `Description` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `CreationDate` datetime NOT NULL DEFAULT current_timestamp();

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `AssignedToId` int(11) NOT NULL;

ALTER TABLE `IncidentResponsePlanTasks` ADD COLUMN IF NOT EXISTS `CreatedById` int(11) NULL;

CREATE INDEX IF NOT EXISTS `IX_IncidentResponsePlanTasks_AssignedToId` ON `IncidentResponsePlanTasks` (`AssignedToId`);

CREATE INDEX IF NOT EXISTS `IX_IncidentResponsePlanTasks_CreatedById` ON `IncidentResponsePlanTasks` (`CreatedById`);

ALTER TABLE `IncidentResponsePlanTasks` ADD CONSTRAINT `fk_irpt_created_by` FOREIGN KEY IF NOT EXISTS (`CreatedById`) REFERENCES `user` (`value`);

ALTER TABLE `IncidentResponsePlanTasks` ADD CONSTRAINT `fk_irpt_task_assigned_to` FOREIGN KEY IF NOT EXISTS (`AssignedToId`) REFERENCES `entities` (`Id`) ON DELETE CASCADE;

ALTER TABLE `IncidentResponsePlanTasks` ADD CONSTRAINT `fk_irpt_updated_by` FOREIGN KEY IF NOT EXISTS (`UpdatedById`) REFERENCES `user` (`value`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241125141031_fixIncidentResponseTask', '8.0.10')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);



ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `TaskType` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `SuccessCriteria` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `ConditionToProceed` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241125142642_fixIncidentResponseTask3', '8.0.10')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

