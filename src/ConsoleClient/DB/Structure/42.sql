-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Guarded so re-running this version converges: `IncidentResponsePlanTaskExecution` is renamed further down this script, so a retry finds it under its new name.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'IncidentResponsePlanTaskExecution') > 0,
                 'ALTER TABLE `IncidentResponsePlanTaskExecution` DROP FOREIGN KEY `fk_irpt_executions_created_by`', 'DO 0');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the table is already named `IncidentResponsePlanTaskExecutions`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'IncidentResponsePlanTaskExecutions') > 0,
                 'DO 0', 'ALTER TABLE `IncidentResponsePlanTaskExecution` RENAME `IncidentResponsePlanTaskExecutions`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_IncidentResponsePlanTaskExecutions_TaskId`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'IncidentResponsePlanTaskExecutions' AND BINARY INDEX_NAME = 'IX_IncidentResponsePlanTaskExecutions_TaskId') > 0,
                 'DO 0', 'ALTER TABLE `IncidentResponsePlanTaskExecutions` RENAME INDEX `IX_IncidentResponsePlanTaskExecution_TaskId` TO `IX_IncidentResponsePlanTaskExecutions_TaskId`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_IncidentResponsePlanTaskExecutions_PlanExecutionId`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'IncidentResponsePlanTaskExecutions' AND BINARY INDEX_NAME = 'IX_IncidentResponsePlanTaskExecutions_PlanExecutionId') > 0,
                 'DO 0', 'ALTER TABLE `IncidentResponsePlanTaskExecutions` RENAME INDEX `IX_IncidentResponsePlanTaskExecution_PlanExecutionId` TO `IX_IncidentResponsePlanTaskExecutions_PlanExecutionId`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_IncidentResponsePlanTaskExecutions_ExecutedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'IncidentResponsePlanTaskExecutions' AND BINARY INDEX_NAME = 'IX_IncidentResponsePlanTaskExecutions_ExecutedById') > 0,
                 'DO 0', 'ALTER TABLE `IncidentResponsePlanTaskExecutions` RENAME INDEX `IX_IncidentResponsePlanTaskExecution_ExecutedById` TO `IX_IncidentResponsePlanTaskExecutions_ExecutedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_IncidentResponsePlanTaskExecutions_CreatedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'IncidentResponsePlanTaskExecutions' AND BINARY INDEX_NAME = 'IX_IncidentResponsePlanTaskExecutions_CreatedById') > 0,
                 'DO 0', 'ALTER TABLE `IncidentResponsePlanTaskExecutions` RENAME INDEX `IX_IncidentResponsePlanTaskExecution_CreatedById` TO `IX_IncidentResponsePlanTaskExecutions_CreatedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

ALTER TABLE `IncidentResponsePlanTaskExecutions` MODIFY COLUMN `CreatedById` int(11) NULL;

ALTER TABLE `IncidentResponsePlanTaskExecutions` ADD COLUMN IF NOT EXISTS `CreatedAt` datetime NOT NULL DEFAULT current_timestamp();

ALTER TABLE `IncidentResponsePlanTaskExecutions` ADD COLUMN IF NOT EXISTS `LastUpdatedAt` datetime NULL DEFAULT current_timestamp();

ALTER TABLE `IncidentResponsePlanTaskExecutions` ADD COLUMN IF NOT EXISTS `LastUpdatedById` int(11) NULL;

CREATE INDEX IF NOT EXISTS `IX_IncidentResponsePlanTaskExecutions_LastUpdatedById` ON `IncidentResponsePlanTaskExecutions` (`LastUpdatedById`);

ALTER TABLE `IncidentResponsePlanTaskExecutions` ADD CONSTRAINT `fk_irpt_executions_created_by` FOREIGN KEY IF NOT EXISTS (`CreatedById`) REFERENCES `user` (`value`);

ALTER TABLE `IncidentResponsePlanTaskExecutions` ADD CONSTRAINT `fk_irpt_executions_last_updated_by` FOREIGN KEY IF NOT EXISTS (`LastUpdatedById`) REFERENCES `user` (`value`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241126175227_IncidentResponsePlanTaskExecution3', '8.0.10')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

