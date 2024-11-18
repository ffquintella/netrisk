START TRANSACTION;

ALTER TABLE `risks` ADD `IncidentResponsePlanId` int NULL;

ALTER TABLE `nr_files` ADD `IncidentResponsePlanExecutionId` int NULL;

ALTER TABLE `nr_files` ADD `IncidentResponsePlanId` int NULL;

ALTER TABLE `nr_files` ADD `IncidentResponsePlanTaskId` int NULL;

CREATE TABLE `IncidentResponsePlans` (
                                         `Id` int NOT NULL AUTO_INCREMENT,
                                         `Name` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                         `Description` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                         `CreationDate` datetime NOT NULL DEFAULT current_timestamp(),
                                         `CreatedById` int(11) NOT NULL,
                                         `LastUpdate` datetime NOT NULL DEFAULT current_timestamp(),
                                         `UpdatedById` int(11) NULL,
                                         `Status` int NOT NULL,
                                         `Notes` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL,
                                         `HasBeenTested` tinyint(1) NOT NULL,
                                         `HasBeenUpdated` tinyint(1) NOT NULL,
                                         `HasBeenExercised` tinyint(1) NOT NULL,
                                         `HasBeenReviewed` tinyint(1) NOT NULL,
                                         `HasBeenApproved` tinyint(1) NOT NULL,
                                         `LastTestDate` datetime NULL,
                                         `LastExerciseDate` datetime NULL,
                                         `LastReviewDate` datetime NULL,
                                         `ApprovalDate` datetime NULL,
                                         `LastTestedById` int(11) NOT NULL,
                                         `LastExercisedById` int(11) NOT NULL,
                                         `LastReviewedById` int(11) NOT NULL,
                                         CONSTRAINT `PRIMARY` PRIMARY KEY (`Id`),
                                         CONSTRAINT `fk_irp_last_exercised_by` FOREIGN KEY (`LastExercisedById`) REFERENCES `entities` (`Id`) ON DELETE CASCADE,
                                         CONSTRAINT `fk_irp_last_reviewed_by` FOREIGN KEY (`LastReviewedById`) REFERENCES `entities` (`Id`) ON DELETE CASCADE,
                                         CONSTRAINT `fk_irp_last_tested_by` FOREIGN KEY (`LastTestedById`) REFERENCES `entities` (`Id`) ON DELETE CASCADE,
                                         CONSTRAINT `fk_irp_user` FOREIGN KEY (`CreatedById`) REFERENCES `user` (`value`),
                                         CONSTRAINT `fk_irp_user_update` FOREIGN KEY (`UpdatedById`) REFERENCES `user` (`value`)
) CHARACTER SET=utf8mb3 COLLATE=utf8mb3_general_ci;

CREATE TABLE `IncidentResponsePlanTasks` (
                                             `Id` int NOT NULL AUTO_INCREMENT,
                                             `Description` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                             `CreationDate` datetime(6) NOT NULL,
                                             `CreatedByValue` int(11) NOT NULL,
                                             `LastUpdate` datetime(6) NOT NULL,
                                             `UpdatedByValue` int(11) NULL,
                                             `Status` int NOT NULL,
                                             `Notes` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL,
                                             `HasBeenTested` tinyint(1) NOT NULL,
                                             `PlanId` int NOT NULL,
                                             `ExecutionOrder` int NOT NULL,
                                             `LastTestDate` datetime(6) NULL,
                                             `LastTestedById` int(11) NULL,
                                             `EstimatedDuration` time(6) NULL,
                                             `LastActualDuration` time(6) NULL,
                                             `AssignedToId` int NOT NULL,
                                             `Priority` int NOT NULL,
                                             `IsParallel` tinyint(1) NOT NULL,
                                             `IsSequential` tinyint(1) NOT NULL,
                                             `IsOptional` tinyint(1) NOT NULL,
                                             `SuccessCriteria` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                             `FailureCriteria` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL,
                                             `CompletionCriteria` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL,
                                             `VerificationCriteria` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL,
                                             `TaskType` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                             `ConditionToProceed` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                             `ConditionToSkip` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL,
                                             CONSTRAINT `PRIMARY` PRIMARY KEY (`Id`),
                                             CONSTRAINT `FK_IncidentResponsePlanTasks_user_CreatedByValue` FOREIGN KEY (`CreatedByValue`) REFERENCES `user` (`value`) ON DELETE CASCADE,
                                             CONSTRAINT `FK_IncidentResponsePlanTasks_user_UpdatedByValue` FOREIGN KEY (`UpdatedByValue`) REFERENCES `user` (`value`),
                                             CONSTRAINT `fk_irp_task_last_tested_by` FOREIGN KEY (`LastTestedById`) REFERENCES `entities` (`Id`),
                                             CONSTRAINT `fk_irp_task_plan` FOREIGN KEY (`PlanId`) REFERENCES `IncidentResponsePlans` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb3 COLLATE=utf8mb3_general_ci;

CREATE TABLE `IncidentResponsePlanExecutions` (
                                                  `Id` int NOT NULL AUTO_INCREMENT,
                                                  `PlanId` int NOT NULL,
                                                  `ExecutionDate` datetime(6) NOT NULL,
                                                  `Duration` time(6) NOT NULL,
                                                  `ExecutedById` int(11) NULL,
                                                  `Notes` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL,
                                                  `Status` int NOT NULL,
                                                  `IsTest` tinyint(1) NOT NULL,
                                                  `IsExercise` tinyint(1) NOT NULL,
                                                  `ExecutionTrigger` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                                  `ExecutionResult` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                                  `TaskId` int NULL,
                                                  CONSTRAINT `PRIMARY` PRIMARY KEY (`Id`),
                                                  CONSTRAINT `fk_irp_executions_plan` FOREIGN KEY (`PlanId`) REFERENCES `IncidentResponsePlans` (`Id`) ON DELETE CASCADE,
                                                  CONSTRAINT `fk_irp_executions_user` FOREIGN KEY (`ExecutedById`) REFERENCES `entities` (`Id`),
                                                  CONSTRAINT `fk_irp_task_executions` FOREIGN KEY (`TaskId`) REFERENCES `IncidentResponsePlanTasks` (`Id`)
) CHARACTER SET=utf8mb3 COLLATE=utf8mb3_general_ci;

CREATE TABLE `IncidentResponsePlanTaskToEntity` (
                                                    `IncidentResponsePlanTaskId` int(11) NOT NULL,
                                                    `EntityId` int(11) NOT NULL,
                                                    CONSTRAINT `PRIMARY` PRIMARY KEY (`IncidentResponsePlanTaskId`, `EntityId`),
                                                    CONSTRAINT `fk_irt_entity_irt` FOREIGN KEY (`EntityId`) REFERENCES `entities` (`Id`) ON DELETE CASCADE,
                                                    CONSTRAINT `fk_irt_irt_entity` FOREIGN KEY (`IncidentResponsePlanTaskId`) REFERENCES `IncidentResponsePlanTasks` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb3 COLLATE=utf8mb3_general_ci;

CREATE INDEX `IX_risks_IncidentResponsePlanId` ON `risks` (`IncidentResponsePlanId`);

CREATE INDEX `IX_nr_files_IncidentResponsePlanExecutionId` ON `nr_files` (`IncidentResponsePlanExecutionId`);

CREATE INDEX `IX_nr_files_IncidentResponsePlanId` ON `nr_files` (`IncidentResponsePlanId`);

CREATE INDEX `IX_nr_files_IncidentResponsePlanTaskId` ON `nr_files` (`IncidentResponsePlanTaskId`);

CREATE INDEX `IX_IncidentResponsePlanExecutions_ExecutedById` ON `IncidentResponsePlanExecutions` (`ExecutedById`);

CREATE INDEX `IX_IncidentResponsePlanExecutions_PlanId` ON `IncidentResponsePlanExecutions` (`PlanId`);

CREATE INDEX `IX_IncidentResponsePlanExecutions_TaskId` ON `IncidentResponsePlanExecutions` (`TaskId`);

CREATE FULLTEXT INDEX `idx_irp_name` ON `IncidentResponsePlans` (`Name`);

CREATE INDEX `IX_IncidentResponsePlans_CreatedById` ON `IncidentResponsePlans` (`CreatedById`);

CREATE INDEX `IX_IncidentResponsePlans_LastExercisedById` ON `IncidentResponsePlans` (`LastExercisedById`);

CREATE INDEX `IX_IncidentResponsePlans_LastReviewedById` ON `IncidentResponsePlans` (`LastReviewedById`);

CREATE INDEX `IX_IncidentResponsePlans_LastTestedById` ON `IncidentResponsePlans` (`LastTestedById`);

CREATE INDEX `IX_IncidentResponsePlans_UpdatedById` ON `IncidentResponsePlans` (`UpdatedById`);

CREATE INDEX `IX_IncidentResponsePlanTasks_CreatedByValue` ON `IncidentResponsePlanTasks` (`CreatedByValue`);

CREATE INDEX `IX_IncidentResponsePlanTasks_LastTestedById` ON `IncidentResponsePlanTasks` (`LastTestedById`);

CREATE INDEX `IX_IncidentResponsePlanTasks_PlanId` ON `IncidentResponsePlanTasks` (`PlanId`);

CREATE INDEX `IX_IncidentResponsePlanTasks_UpdatedByValue` ON `IncidentResponsePlanTasks` (`UpdatedByValue`);

CREATE INDEX `irt_id` ON `IncidentResponsePlanTaskToEntity` (`EntityId`, `IncidentResponsePlanTaskId`);

ALTER TABLE `nr_files` ADD CONSTRAINT `fk_irp_attachments` FOREIGN KEY (`IncidentResponsePlanId`) REFERENCES `IncidentResponsePlans` (`Id`);

ALTER TABLE `nr_files` ADD CONSTRAINT `fk_irp_executions_attachments` FOREIGN KEY (`IncidentResponsePlanExecutionId`) REFERENCES `IncidentResponsePlanExecutions` (`Id`);

ALTER TABLE `nr_files` ADD CONSTRAINT `fk_irp_task_attachments` FOREIGN KEY (`IncidentResponsePlanTaskId`) REFERENCES `IncidentResponsePlanTasks` (`Id`);

ALTER TABLE `risks` ADD CONSTRAINT `FK_risks_IncidentResponsePlans_IncidentResponsePlanId` FOREIGN KEY (`IncidentResponsePlanId`) REFERENCES `IncidentResponsePlans` (`Id`);

COMMIT;