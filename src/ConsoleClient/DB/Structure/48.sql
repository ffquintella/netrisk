START TRANSACTION;
ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `TaskType` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL;

CREATE FULLTEXT INDEX `idx_irpt_task_type` ON `IncidentResponsePlanTasks` (`TaskType`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241216174318_IRPTTaskType', '9.0.0');

COMMIT;