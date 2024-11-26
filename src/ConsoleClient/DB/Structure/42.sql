START TRANSACTION;

ALTER TABLE `IncidentResponsePlanTaskExecution` DROP FOREIGN KEY `fk_irpt_executions_created_by`;

ALTER TABLE `IncidentResponsePlanTaskExecution` RENAME `IncidentResponsePlanTaskExecutions`;

ALTER TABLE `IncidentResponsePlanTaskExecutions` RENAME INDEX `IX_IncidentResponsePlanTaskExecution_TaskId` TO `IX_IncidentResponsePlanTaskExecutions_TaskId`;

ALTER TABLE `IncidentResponsePlanTaskExecutions` RENAME INDEX `IX_IncidentResponsePlanTaskExecution_PlanExecutionId` TO `IX_IncidentResponsePlanTaskExecutions_PlanExecutionId`;

ALTER TABLE `IncidentResponsePlanTaskExecutions` RENAME INDEX `IX_IncidentResponsePlanTaskExecution_ExecutedById` TO `IX_IncidentResponsePlanTaskExecutions_ExecutedById`;

ALTER TABLE `IncidentResponsePlanTaskExecutions` RENAME INDEX `IX_IncidentResponsePlanTaskExecution_CreatedById` TO `IX_IncidentResponsePlanTaskExecutions_CreatedById`;

ALTER TABLE `IncidentResponsePlanTaskExecutions` MODIFY COLUMN `CreatedById` int(11) NULL;

ALTER TABLE `IncidentResponsePlanTaskExecutions` ADD `CreatedAt` datetime NOT NULL DEFAULT current_timestamp();

ALTER TABLE `IncidentResponsePlanTaskExecutions` ADD `LastUpdatedAt` datetime NULL DEFAULT current_timestamp();

ALTER TABLE `IncidentResponsePlanTaskExecutions` ADD `LastUpdatedById` int(11) NULL;

CREATE INDEX `IX_IncidentResponsePlanTaskExecutions_LastUpdatedById` ON `IncidentResponsePlanTaskExecutions` (`LastUpdatedById`);

ALTER TABLE `IncidentResponsePlanTaskExecutions` ADD CONSTRAINT `fk_irpt_executions_created_by` FOREIGN KEY (`CreatedById`) REFERENCES `user` (`value`);

ALTER TABLE `IncidentResponsePlanTaskExecutions` ADD CONSTRAINT `fk_irpt_executions_last_updated_by` FOREIGN KEY (`LastUpdatedById`) REFERENCES `user` (`value`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241126175227_IncidentResponsePlanTaskExecution3', '8.0.10');

COMMIT;
