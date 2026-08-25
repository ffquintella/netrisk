-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `TaskType` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL;

CREATE FULLTEXT INDEX IF NOT EXISTS `idx_irpt_task_type` ON `IncidentResponsePlanTasks` (`TaskType`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241216174318_IRPTTaskType', '9.0.0')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

