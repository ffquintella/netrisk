START TRANSACTION;
-- Track 6 — Phase 2: snake_case naming uniformization (table + column + index renames).
-- Hand-assembled from EF migration Track6Phase2NamingUniformization, with Pomelo's
-- DELIMITER-based PK procedure removed (the join-table PK is composite, not auto_increment),
-- so the numbered-SQL applier (MySqlConnector) can run it. Renames only — no data loss.

ALTER TABLE `FaceIDUsers` DROP FOREIGN KEY `FK_FaceIDUsers_user_UserId`;

ALTER TABLE `IncidentToIncidentResponsePlan` DROP FOREIGN KEY `FK_IncidentToIncidentResponsePlan_IncidentResponsePlans_Inciden~`;

ALTER TABLE `IncidentToIncidentResponsePlan` DROP FOREIGN KEY `FK_IncidentToIncidentResponsePlan_Incidents_IncidentId`;

ALTER TABLE `IncidentToIncidentResponsePlan` DROP PRIMARY KEY;

ALTER TABLE `Incidents` RENAME `incidents`;

ALTER TABLE `IncidentToIncidentResponsePlan` RENAME `incident_to_incident_response_plan`;

ALTER TABLE `IncidentResponsePlanTasks` RENAME `incident_response_plan_tasks`;

ALTER TABLE `IncidentResponsePlanTaskExecutions` RENAME `incident_response_plan_task_executions`;

ALTER TABLE `IncidentResponsePlans` RENAME `incident_response_plans`;

ALTER TABLE `IncidentResponsePlanExecutions` RENAME `incident_response_plan_executions`;

ALTER TABLE `FixRequest` RENAME `fix_requests`;

ALTER TABLE `FaceIDUsers` RENAME `face_id_users`;

ALTER TABLE `BiometricTransaction` RENAME `biometric_transactions`;

ALTER TABLE `vulnerabilities_to_actions` RENAME COLUMN `actionId` TO `action_id`;

ALTER TABLE `vulnerabilities_to_actions` RENAME COLUMN `vulnerabilityId` TO `vulnerability_id`;

ALTER TABLE `reports` RENAME COLUMN `fileId` TO `file_id`;

ALTER TABLE `reports` RENAME COLUMN `creatorId` TO `creator_id`;

ALTER TABLE `reports` RENAME COLUMN `creationDate` TO `created_at`;

ALTER TABLE `messages` RENAME COLUMN `Message` TO `message`;

ALTER TABLE `incidents` RENAME INDEX `IX_Incidents_UpdatedById` TO `IX_incidents_UpdatedById`;

ALTER TABLE `incidents` RENAME INDEX `IX_Incidents_ReportEntityId` TO `IX_incidents_ReportEntityId`;

ALTER TABLE `incidents` RENAME INDEX `IX_Incidents_ImpactedEntityId` TO `IX_incidents_ImpactedEntityId`;

ALTER TABLE `incidents` RENAME INDEX `IX_Incidents_CreatedById` TO `IX_incidents_CreatedById`;

ALTER TABLE `incidents` RENAME INDEX `IX_Incidents_AssignedToId` TO `IX_incidents_AssignedToId`;

ALTER TABLE `hosts` RENAME COLUMN `OS` TO `os`;

ALTER TABLE `hosts` RENAME COLUMN `FQDN` TO `fqdn`;

ALTER TABLE `incident_to_incident_response_plan` RENAME INDEX `IX_IncidentToIncidentResponsePlan_IncidentResponsePlanId` TO `IX_incident_to_incident_response_plan_IncidentResponsePlanId`;

ALTER TABLE `incident_response_plan_tasks` RENAME INDEX `IX_IncidentResponsePlanTasks_UpdatedById` TO `IX_incident_response_plan_tasks_UpdatedById`;

ALTER TABLE `incident_response_plan_tasks` RENAME INDEX `IX_IncidentResponsePlanTasks_PlanId` TO `IX_incident_response_plan_tasks_PlanId`;

ALTER TABLE `incident_response_plan_tasks` RENAME INDEX `IX_IncidentResponsePlanTasks_LastTestedById` TO `IX_incident_response_plan_tasks_LastTestedById`;

ALTER TABLE `incident_response_plan_tasks` RENAME INDEX `IX_IncidentResponsePlanTasks_CreatedById` TO `IX_incident_response_plan_tasks_CreatedById`;

ALTER TABLE `incident_response_plan_tasks` RENAME INDEX `IX_IncidentResponsePlanTasks_AssignedToId` TO `IX_incident_response_plan_tasks_AssignedToId`;

ALTER TABLE `incident_response_plan_task_executions` RENAME INDEX `IX_IncidentResponsePlanTaskExecutions_TaskId` TO `IX_incident_response_plan_task_executions_TaskId`;

ALTER TABLE `incident_response_plan_task_executions` RENAME INDEX `IX_IncidentResponsePlanTaskExecutions_PlanExecutionId` TO `IX_incident_response_plan_task_executions_PlanExecutionId`;

ALTER TABLE `incident_response_plan_task_executions` RENAME INDEX `IX_IncidentResponsePlanTaskExecutions_LastUpdatedById` TO `IX_incident_response_plan_task_executions_LastUpdatedById`;

ALTER TABLE `incident_response_plan_task_executions` RENAME INDEX `IX_IncidentResponsePlanTaskExecutions_ExecutedById` TO `IX_incident_response_plan_task_executions_ExecutedById`;

ALTER TABLE `incident_response_plan_task_executions` RENAME INDEX `IX_IncidentResponsePlanTaskExecutions_CreatedById` TO `IX_incident_response_plan_task_executions_CreatedById`;

ALTER TABLE `incident_response_plans` RENAME INDEX `IX_IncidentResponsePlans_UpdatedById` TO `IX_incident_response_plans_UpdatedById`;

ALTER TABLE `incident_response_plans` RENAME INDEX `IX_IncidentResponsePlans_LastTestedById` TO `IX_incident_response_plans_LastTestedById`;

ALTER TABLE `incident_response_plans` RENAME INDEX `IX_IncidentResponsePlans_LastReviewedById` TO `IX_incident_response_plans_LastReviewedById`;

ALTER TABLE `incident_response_plans` RENAME INDEX `IX_IncidentResponsePlans_LastExercisedById` TO `IX_incident_response_plans_LastExercisedById`;

ALTER TABLE `incident_response_plans` RENAME INDEX `IX_IncidentResponsePlans_CreatedById` TO `IX_incident_response_plans_CreatedById`;

ALTER TABLE `incident_response_plans` RENAME INDEX `IX_IncidentResponsePlans_ApprovedById` TO `IX_incident_response_plans_ApprovedById`;

ALTER TABLE `incident_response_plan_executions` RENAME INDEX `IX_IncidentResponsePlanExecutions_PlanId` TO `IX_incident_response_plan_executions_PlanId`;

ALTER TABLE `incident_response_plan_executions` RENAME INDEX `IX_IncidentResponsePlanExecutions_LastUpdatedById` TO `IX_incident_response_plan_executions_LastUpdatedById`;

ALTER TABLE `incident_response_plan_executions` RENAME INDEX `IX_IncidentResponsePlanExecutions_ExecutedById` TO `IX_incident_response_plan_executions_ExecutedById`;

ALTER TABLE `incident_response_plan_executions` RENAME INDEX `IX_IncidentResponsePlanExecutions_CreatedById` TO `IX_incident_response_plan_executions_CreatedById`;

ALTER TABLE `face_id_users` RENAME INDEX `IX_FaceIDUsers_UserId` TO `IX_face_id_users_UserId`;

ALTER TABLE `face_id_users` RENAME INDEX `IX_FaceIDUsers_LastUpdateUserId` TO `IX_face_id_users_LastUpdateUserId`;

ALTER TABLE `biometric_transactions` RENAME INDEX `IX_BiometricTransaction_UserId` TO `IX_biometric_transactions_UserId`;

ALTER TABLE `biometric_transactions` RENAME INDEX `IX_BiometricTransaction_FaceIdUserId` TO `IX_biometric_transactions_FaceIdUserId`;

ALTER TABLE `incident_to_incident_response_plan` ADD CONSTRAINT `PK_incident_to_incident_response_plan` PRIMARY KEY (`IncidentId`, `IncidentResponsePlanId`);

ALTER TABLE `face_id_users` ADD CONSTRAINT `FK_face_id_users_user_UserId` FOREIGN KEY (`UserId`) REFERENCES `user` (`value`) ON DELETE CASCADE;

ALTER TABLE `incident_to_incident_response_plan` ADD CONSTRAINT `FK_incident_to_incident_response_plan_incident_response_plans_I~` FOREIGN KEY (`IncidentResponsePlanId`) REFERENCES `incident_response_plans` (`Id`) ON DELETE CASCADE;

ALTER TABLE `incident_to_incident_response_plan` ADD CONSTRAINT `FK_incident_to_incident_response_plan_incidents_IncidentId` FOREIGN KEY (`IncidentId`) REFERENCES `incidents` (`Id`) ON DELETE CASCADE;
COMMIT;
