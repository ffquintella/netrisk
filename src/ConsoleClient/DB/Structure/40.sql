-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

ALTER TABLE `IncidentResponsePlanExecutions` DROP FOREIGN KEY IF EXISTS `fk_irp_task_executions`;

ALTER TABLE `IncidentResponsePlanExecutions` DROP INDEX IF EXISTS `IX_IncidentResponsePlanExecutions_TaskId`;

ALTER TABLE `IncidentResponsePlanExecutions` DROP COLUMN IF EXISTS `TaskId`;

ALTER TABLE `nr_files` ADD COLUMN IF NOT EXISTS `IncidentResponsePlanTaskExecutionId` int NULL;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `Status` int(11) NOT NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlanExecutions` MODIFY COLUMN `Status` int(11) NOT NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlanExecutions` ADD COLUMN IF NOT EXISTS `CreatedById` int(11) NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS `IncidentResponsePlanTaskExecution` (
                                                     `Id` int NOT NULL AUTO_INCREMENT,
                                                     `PlanExecutionId` int(11) NOT NULL,
                                                     `TaskId` int(11) NOT NULL,
                                                     `ExecutionDate` datetime NOT NULL DEFAULT current_timestamp(),
                                                     `Duration` bigint NOT NULL,
                                                     `ExecutedById` int(11) NULL,
                                                     `CreatedById` int(11) NOT NULL,
                                                     `Notes` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL,
                                                     `Status` int(11) NOT NULL DEFAULT 0,
                                                     `IsTest` tinyint(1) NULL DEFAULT 0,
                                                     `IsExercise` tinyint(1) NULL DEFAULT 0,
                                                     CONSTRAINT `PRIMARY` PRIMARY KEY (`Id`),
                                                     CONSTRAINT `fk_irpt_executions_created_by` FOREIGN KEY (`CreatedById`) REFERENCES `user` (`value`) ON DELETE CASCADE,
                                                     CONSTRAINT `fk_irpt_executions_entity` FOREIGN KEY (`ExecutedById`) REFERENCES `entities` (`Id`),
                                                     CONSTRAINT `fk_irpt_executions_plan` FOREIGN KEY (`PlanExecutionId`) REFERENCES `IncidentResponsePlanExecutions` (`Id`) ON DELETE CASCADE,
                                                     CONSTRAINT `fk_irpt_executions_task` FOREIGN KEY (`TaskId`) REFERENCES `IncidentResponsePlanTasks` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE INDEX IF NOT EXISTS `IX_nr_files_IncidentResponsePlanTaskExecutionId` ON `nr_files` (`IncidentResponsePlanTaskExecutionId`);

CREATE INDEX IF NOT EXISTS `idx_irpt_status2` ON `IncidentResponsePlanTasks` (`Status`);

CREATE INDEX IF NOT EXISTS `idx_irpt_status` ON `IncidentResponsePlans` (`Status`);

CREATE INDEX IF NOT EXISTS `idx_irpt_status1` ON `IncidentResponsePlanExecutions` (`Status`);

CREATE INDEX IF NOT EXISTS `IX_IncidentResponsePlanExecutions_CreatedById` ON `IncidentResponsePlanExecutions` (`CreatedById`);

CREATE INDEX IF NOT EXISTS `idx_irpt_exec_status` ON `IncidentResponsePlanTaskExecution` (`Status`);

CREATE INDEX IF NOT EXISTS `IX_IncidentResponsePlanTaskExecution_CreatedById` ON `IncidentResponsePlanTaskExecution` (`CreatedById`);

CREATE INDEX IF NOT EXISTS `IX_IncidentResponsePlanTaskExecution_ExecutedById` ON `IncidentResponsePlanTaskExecution` (`ExecutedById`);

CREATE INDEX IF NOT EXISTS `IX_IncidentResponsePlanTaskExecution_PlanExecutionId` ON `IncidentResponsePlanTaskExecution` (`PlanExecutionId`);

CREATE INDEX IF NOT EXISTS `IX_IncidentResponsePlanTaskExecution_TaskId` ON `IncidentResponsePlanTaskExecution` (`TaskId`);

ALTER TABLE `IncidentResponsePlanExecutions` ADD CONSTRAINT `fk_irp_executions_created_by` FOREIGN KEY IF NOT EXISTS (`CreatedById`) REFERENCES `user` (`value`) ON DELETE CASCADE;

ALTER TABLE `nr_files` ADD CONSTRAINT `fk_irpt_executions_attachments` FOREIGN KEY IF NOT EXISTS (`IncidentResponsePlanTaskExecutionId`) REFERENCES `IncidentResponsePlanTaskExecution` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241126140506_IncidentResponsePlanTaskExecution', '8.0.10')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

