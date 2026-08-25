-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

ALTER TABLE `IncidentResponsePlanExecutions` MODIFY COLUMN `Notes` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL;

ALTER TABLE `IncidentResponsePlanExecutions` MODIFY COLUMN `IsTest` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlanExecutions` MODIFY COLUMN `IsExercise` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlanExecutions` MODIFY COLUMN `ExecutionTrigger` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL;

ALTER TABLE `IncidentResponsePlanExecutions` MODIFY COLUMN `ExecutionResult` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL;

ALTER TABLE `IncidentResponsePlanExecutions` MODIFY COLUMN `ExecutionDate` datetime NULL DEFAULT current_timestamp();

ALTER TABLE `IncidentResponsePlanExecutions` MODIFY COLUMN `Duration` bigint NOT NULL;

ALTER TABLE `IncidentResponsePlanExecutions` ADD COLUMN IF NOT EXISTS `CreationDate` datetime NULL DEFAULT current_timestamp();

ALTER TABLE `IncidentResponsePlanExecutions` ADD COLUMN IF NOT EXISTS `LastUpdateDate` datetime NULL;

ALTER TABLE `IncidentResponsePlanExecutions` ADD COLUMN IF NOT EXISTS `LastUpdatedById` int(11) NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS `IX_IncidentResponsePlanExecutions_LastUpdatedById` ON `IncidentResponsePlanExecutions` (`LastUpdatedById`);

ALTER TABLE `IncidentResponsePlanExecutions` ADD CONSTRAINT `fk_irp_executions_updated_by` FOREIGN KEY IF NOT EXISTS (`LastUpdatedById`) REFERENCES `user` (`value`) ON DELETE CASCADE;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241126152243_IncidentResponsePlanTaskExecution2', '8.0.10')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

