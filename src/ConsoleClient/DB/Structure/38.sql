START TRANSACTION;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenUpdated` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenTested` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenReviewed` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenExercised` tinyint(1) NULL DEFAULT 0;

ALTER TABLE `IncidentResponsePlans` MODIFY COLUMN `HasBeenApproved` tinyint(1) NULL DEFAULT 0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241121143359_fixIncidentResponsePlan2', '8.0.10');

COMMIT;

START TRANSACTION;

ALTER TABLE `IncidentResponsePlanTasks` DROP FOREIGN KEY `FK_IncidentResponsePlanTasks_user_CreatedByValue`;

ALTER TABLE `IncidentResponsePlanTasks` DROP FOREIGN KEY `FK_IncidentResponsePlanTasks_user_UpdatedByValue`;

DROP TABLE `IncidentResponsePlanTaskToEntity`;

ALTER TABLE `IncidentResponsePlanTasks` DROP INDEX `IX_IncidentResponsePlanTasks_CreatedByValue`;

ALTER TABLE `IncidentResponsePlanTasks` DROP COLUMN `CreatedByValue`;

ALTER TABLE `IncidentResponsePlanTasks` RENAME COLUMN `UpdatedByValue` TO `UpdatedById`;

ALTER TABLE `IncidentResponsePlanTasks` RENAME INDEX `IX_IncidentResponsePlanTasks_UpdatedByValue` TO `IX_IncidentResponsePlanTasks_UpdatedById`;

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

ALTER TABLE `IncidentResponsePlanTasks` ADD `CreatedById` int(11) NULL;

CREATE INDEX `IX_IncidentResponsePlanTasks_AssignedToId` ON `IncidentResponsePlanTasks` (`AssignedToId`);

CREATE INDEX `IX_IncidentResponsePlanTasks_CreatedById` ON `IncidentResponsePlanTasks` (`CreatedById`);

ALTER TABLE `IncidentResponsePlanTasks` ADD CONSTRAINT `fk_irpt_created_by` FOREIGN KEY (`CreatedById`) REFERENCES `user` (`value`);

ALTER TABLE `IncidentResponsePlanTasks` ADD CONSTRAINT `fk_irpt_task_assigned_to` FOREIGN KEY (`AssignedToId`) REFERENCES `entities` (`Id`) ON DELETE CASCADE;

ALTER TABLE `IncidentResponsePlanTasks` ADD CONSTRAINT `fk_irpt_updated_by` FOREIGN KEY (`UpdatedById`) REFERENCES `user` (`value`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241125141031_fixIncidentResponseTask', '8.0.10');

COMMIT;

START TRANSACTION;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `TaskType` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `SuccessCriteria` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL;

ALTER TABLE `IncidentResponsePlanTasks` MODIFY COLUMN `ConditionToProceed` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241125142642_fixIncidentResponseTask3', '8.0.10');

COMMIT;