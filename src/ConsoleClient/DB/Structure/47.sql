START TRANSACTION;
ALTER TABLE `IncidentResponsePlanTasks` RENAME INDEX `idx_irpt_status2` TO `idx_irpt_status1`;

ALTER TABLE `IncidentResponsePlans` RENAME INDEX `idx_irpt_status` TO `idx_irp_status`;

ALTER TABLE `IncidentResponsePlanExecutions` RENAME INDEX `idx_irpt_status1` TO `idx_irpt_status`;

ALTER TABLE `IncidentResponsePlanTasks` ADD `Name` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT '';

CREATE INDEX `idx_irpt_exec_order` ON `IncidentResponsePlanTasks` (`ExecutionOrder`);

CREATE FULLTEXT INDEX `idx_irpt_name` ON `IncidentResponsePlanTasks` (`Name`);

CREATE INDEX `idx_irpt_optinal` ON `IncidentResponsePlanTasks` (`IsOptional`);

CREATE INDEX `idx_irpt_parallel` ON `IncidentResponsePlanTasks` (`IsParallel`);

CREATE INDEX `idx_irpt_priority` ON `IncidentResponsePlanTasks` (`Priority`);

CREATE INDEX `idx_irpt_sequencial` ON `IncidentResponsePlanTasks` (`IsSequential`);

CREATE INDEX `idx_irp_approved` ON `IncidentResponsePlans` (`HasBeenApproved`);

CREATE INDEX `idx_irp_lupdate` ON `IncidentResponsePlans` (`LastUpdate`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241216164130_IRPTName', '9.0.0');

COMMIT;