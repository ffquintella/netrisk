START TRANSACTION;

ALTER TABLE `IncidentResponsePlanExecutions` DROP FOREIGN KEY `fk_irp_task_executions`;

ALTER TABLE `IncidentResponsePlanExecutions` DROP INDEX `IX_IncidentResponsePlanExecutions_TaskId`;

ALTER TABLE `IncidentResponsePlanExecutions` DROP COLUMN `TaskId`;

ALTER TABLE `nr_files` ADD `IncidentResponsePlanTaskExecutionId` int NULL;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `Status` int(11) NOT NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlanExecutions` MODIFY COLUMN `Status` int(11) NOT NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlanExecutions` ADD `CreatedById` int(11) NOT NULL DEFAULT 0;

CREATE TABLE `IncidentResponsePlanTaskExecution` (
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

CREATE INDEX `IX_nr_files_IncidentResponsePlanTaskExecutionId` ON `nr_files` (`IncidentResponsePlanTaskExecutionId`);

CREATE INDEX `idx_irpt_status2` ON `IncidentResponsePlanTasks` (`Status`);

CREATE INDEX `idx_irpt_status` ON `IncidentResponsePlans` (`Status`);

CREATE INDEX `idx_irpt_status1` ON `IncidentResponsePlanExecutions` (`Status`);

CREATE INDEX `IX_IncidentResponsePlanExecutions_CreatedById` ON `IncidentResponsePlanExecutions` (`CreatedById`);

CREATE INDEX `idx_irpt_exec_status` ON `IncidentResponsePlanTaskExecution` (`Status`);

CREATE INDEX `IX_IncidentResponsePlanTaskExecution_CreatedById` ON `IncidentResponsePlanTaskExecution` (`CreatedById`);

CREATE INDEX `IX_IncidentResponsePlanTaskExecution_ExecutedById` ON `IncidentResponsePlanTaskExecution` (`ExecutedById`);

CREATE INDEX `IX_IncidentResponsePlanTaskExecution_PlanExecutionId` ON `IncidentResponsePlanTaskExecution` (`PlanExecutionId`);

CREATE INDEX `IX_IncidentResponsePlanTaskExecution_TaskId` ON `IncidentResponsePlanTaskExecution` (`TaskId`);

ALTER TABLE `IncidentResponsePlanExecutions` ADD CONSTRAINT `fk_irp_executions_created_by` FOREIGN KEY (`CreatedById`) REFERENCES `user` (`value`) ON DELETE CASCADE;

ALTER TABLE `nr_files` ADD CONSTRAINT `fk_irpt_executions_attachments` FOREIGN KEY (`IncidentResponsePlanTaskExecutionId`) REFERENCES `IncidentResponsePlanTaskExecution` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241126140506_IncidentResponsePlanTaskExecution', '8.0.10');

COMMIT;