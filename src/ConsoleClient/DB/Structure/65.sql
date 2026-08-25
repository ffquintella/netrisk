-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Track 6 — Phase 2: snake_case naming uniformization (table + column + index renames).
-- Hand-assembled from EF migration Track6Phase2NamingUniformization, with Pomelo's
-- DELIMITER-based PK procedure removed (the join-table PK is composite, not auto_increment),
-- so the numbered-SQL applier (MySqlConnector) can run it. Renames only — no data loss.

-- Guarded so re-running this version converges: `FaceIDUsers` is renamed further down this script, so a retry finds it under its new name.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'FaceIDUsers') > 0,
                 'ALTER TABLE `FaceIDUsers` DROP FOREIGN KEY `FK_FaceIDUsers_user_UserId`', 'DO 0');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: `IncidentToIncidentResponsePlan` is renamed further down this script, so a retry finds it under its new name.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'IncidentToIncidentResponsePlan') > 0,
                 'ALTER TABLE `IncidentToIncidentResponsePlan` DROP FOREIGN KEY `FK_IncidentToIncidentResponsePlan_IncidentResponsePlans_Inciden~`', 'DO 0');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: `IncidentToIncidentResponsePlan` is renamed further down this script, so a retry finds it under its new name.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'IncidentToIncidentResponsePlan') > 0,
                 'ALTER TABLE `IncidentToIncidentResponsePlan` DROP FOREIGN KEY `FK_IncidentToIncidentResponsePlan_Incidents_IncidentId`', 'DO 0');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: `IncidentToIncidentResponsePlan` is renamed further down this script, so a retry finds it under its new name.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'IncidentToIncidentResponsePlan') > 0,
                 'ALTER TABLE `IncidentToIncidentResponsePlan` DROP PRIMARY KEY', 'DO 0');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the table is already named `incidents`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incidents') > 0,
                 'DO 0', 'ALTER TABLE `Incidents` RENAME `incidents`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the table is already named `incident_to_incident_response_plan`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_to_incident_response_plan') > 0,
                 'DO 0', 'ALTER TABLE `IncidentToIncidentResponsePlan` RENAME `incident_to_incident_response_plan`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the table is already named `incident_response_plan_tasks`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plan_tasks') > 0,
                 'DO 0', 'ALTER TABLE `IncidentResponsePlanTasks` RENAME `incident_response_plan_tasks`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the table is already named `incident_response_plan_task_executions`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plan_task_executions') > 0,
                 'DO 0', 'ALTER TABLE `IncidentResponsePlanTaskExecutions` RENAME `incident_response_plan_task_executions`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the table is already named `incident_response_plans`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plans') > 0,
                 'DO 0', 'ALTER TABLE `IncidentResponsePlans` RENAME `incident_response_plans`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the table is already named `incident_response_plan_executions`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plan_executions') > 0,
                 'DO 0', 'ALTER TABLE `IncidentResponsePlanExecutions` RENAME `incident_response_plan_executions`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the table is already named `fix_requests`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'fix_requests') > 0,
                 'DO 0', 'ALTER TABLE `FixRequest` RENAME `fix_requests`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the table is already named `face_id_users`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'face_id_users') > 0,
                 'DO 0', 'ALTER TABLE `FaceIDUsers` RENAME `face_id_users`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the table is already named `biometric_transactions`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'biometric_transactions') > 0,
                 'DO 0', 'ALTER TABLE `BiometricTransaction` RENAME `biometric_transactions`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the column is already named `action_id`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.COLUMNS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'vulnerabilities_to_actions' AND BINARY COLUMN_NAME = 'action_id') > 0,
                 'DO 0', 'ALTER TABLE `vulnerabilities_to_actions` RENAME COLUMN `actionId` TO `action_id`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the column is already named `vulnerability_id`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.COLUMNS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'vulnerabilities_to_actions' AND BINARY COLUMN_NAME = 'vulnerability_id') > 0,
                 'DO 0', 'ALTER TABLE `vulnerabilities_to_actions` RENAME COLUMN `vulnerabilityId` TO `vulnerability_id`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the column is already named `file_id`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.COLUMNS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'reports' AND BINARY COLUMN_NAME = 'file_id') > 0,
                 'DO 0', 'ALTER TABLE `reports` RENAME COLUMN `fileId` TO `file_id`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the column is already named `creator_id`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.COLUMNS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'reports' AND BINARY COLUMN_NAME = 'creator_id') > 0,
                 'DO 0', 'ALTER TABLE `reports` RENAME COLUMN `creatorId` TO `creator_id`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the column is already named `created_at`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.COLUMNS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'reports' AND BINARY COLUMN_NAME = 'created_at') > 0,
                 'DO 0', 'ALTER TABLE `reports` RENAME COLUMN `creationDate` TO `created_at`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the column is already named `message`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.COLUMNS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'messages' AND BINARY COLUMN_NAME = 'message') > 0,
                 'DO 0', 'ALTER TABLE `messages` RENAME COLUMN `Message` TO `message`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incidents_UpdatedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incidents' AND BINARY INDEX_NAME = 'IX_incidents_UpdatedById') > 0,
                 'DO 0', 'ALTER TABLE `incidents` RENAME INDEX `IX_Incidents_UpdatedById` TO `IX_incidents_UpdatedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incidents_ReportEntityId`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incidents' AND BINARY INDEX_NAME = 'IX_incidents_ReportEntityId') > 0,
                 'DO 0', 'ALTER TABLE `incidents` RENAME INDEX `IX_Incidents_ReportEntityId` TO `IX_incidents_ReportEntityId`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incidents_ImpactedEntityId`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incidents' AND BINARY INDEX_NAME = 'IX_incidents_ImpactedEntityId') > 0,
                 'DO 0', 'ALTER TABLE `incidents` RENAME INDEX `IX_Incidents_ImpactedEntityId` TO `IX_incidents_ImpactedEntityId`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incidents_CreatedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incidents' AND BINARY INDEX_NAME = 'IX_incidents_CreatedById') > 0,
                 'DO 0', 'ALTER TABLE `incidents` RENAME INDEX `IX_Incidents_CreatedById` TO `IX_incidents_CreatedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incidents_AssignedToId`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incidents' AND BINARY INDEX_NAME = 'IX_incidents_AssignedToId') > 0,
                 'DO 0', 'ALTER TABLE `incidents` RENAME INDEX `IX_Incidents_AssignedToId` TO `IX_incidents_AssignedToId`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the column is already named `os`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.COLUMNS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'hosts' AND BINARY COLUMN_NAME = 'os') > 0,
                 'DO 0', 'ALTER TABLE `hosts` RENAME COLUMN `OS` TO `os`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the column is already named `fqdn`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.COLUMNS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'hosts' AND BINARY COLUMN_NAME = 'fqdn') > 0,
                 'DO 0', 'ALTER TABLE `hosts` RENAME COLUMN `FQDN` TO `fqdn`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_to_incident_response_plan_IncidentResponsePlanId`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_to_incident_response_plan' AND BINARY INDEX_NAME = 'IX_incident_to_incident_response_plan_IncidentResponsePlanId') > 0,
                 'DO 0', 'ALTER TABLE `incident_to_incident_response_plan` RENAME INDEX `IX_IncidentToIncidentResponsePlan_IncidentResponsePlanId` TO `IX_incident_to_incident_response_plan_IncidentResponsePlanId`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plan_tasks_UpdatedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plan_tasks' AND BINARY INDEX_NAME = 'IX_incident_response_plan_tasks_UpdatedById') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plan_tasks` RENAME INDEX `IX_IncidentResponsePlanTasks_UpdatedById` TO `IX_incident_response_plan_tasks_UpdatedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plan_tasks_PlanId`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plan_tasks' AND BINARY INDEX_NAME = 'IX_incident_response_plan_tasks_PlanId') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plan_tasks` RENAME INDEX `IX_IncidentResponsePlanTasks_PlanId` TO `IX_incident_response_plan_tasks_PlanId`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plan_tasks_LastTestedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plan_tasks' AND BINARY INDEX_NAME = 'IX_incident_response_plan_tasks_LastTestedById') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plan_tasks` RENAME INDEX `IX_IncidentResponsePlanTasks_LastTestedById` TO `IX_incident_response_plan_tasks_LastTestedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plan_tasks_CreatedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plan_tasks' AND BINARY INDEX_NAME = 'IX_incident_response_plan_tasks_CreatedById') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plan_tasks` RENAME INDEX `IX_IncidentResponsePlanTasks_CreatedById` TO `IX_incident_response_plan_tasks_CreatedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plan_tasks_AssignedToId`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plan_tasks' AND BINARY INDEX_NAME = 'IX_incident_response_plan_tasks_AssignedToId') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plan_tasks` RENAME INDEX `IX_IncidentResponsePlanTasks_AssignedToId` TO `IX_incident_response_plan_tasks_AssignedToId`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plan_task_executions_TaskId`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plan_task_executions' AND BINARY INDEX_NAME = 'IX_incident_response_plan_task_executions_TaskId') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plan_task_executions` RENAME INDEX `IX_IncidentResponsePlanTaskExecutions_TaskId` TO `IX_incident_response_plan_task_executions_TaskId`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plan_task_executions_PlanExecutionId`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plan_task_executions' AND BINARY INDEX_NAME = 'IX_incident_response_plan_task_executions_PlanExecutionId') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plan_task_executions` RENAME INDEX `IX_IncidentResponsePlanTaskExecutions_PlanExecutionId` TO `IX_incident_response_plan_task_executions_PlanExecutionId`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plan_task_executions_LastUpdatedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plan_task_executions' AND BINARY INDEX_NAME = 'IX_incident_response_plan_task_executions_LastUpdatedById') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plan_task_executions` RENAME INDEX `IX_IncidentResponsePlanTaskExecutions_LastUpdatedById` TO `IX_incident_response_plan_task_executions_LastUpdatedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plan_task_executions_ExecutedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plan_task_executions' AND BINARY INDEX_NAME = 'IX_incident_response_plan_task_executions_ExecutedById') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plan_task_executions` RENAME INDEX `IX_IncidentResponsePlanTaskExecutions_ExecutedById` TO `IX_incident_response_plan_task_executions_ExecutedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plan_task_executions_CreatedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plan_task_executions' AND BINARY INDEX_NAME = 'IX_incident_response_plan_task_executions_CreatedById') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plan_task_executions` RENAME INDEX `IX_IncidentResponsePlanTaskExecutions_CreatedById` TO `IX_incident_response_plan_task_executions_CreatedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plans_UpdatedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plans' AND BINARY INDEX_NAME = 'IX_incident_response_plans_UpdatedById') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plans` RENAME INDEX `IX_IncidentResponsePlans_UpdatedById` TO `IX_incident_response_plans_UpdatedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plans_LastTestedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plans' AND BINARY INDEX_NAME = 'IX_incident_response_plans_LastTestedById') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plans` RENAME INDEX `IX_IncidentResponsePlans_LastTestedById` TO `IX_incident_response_plans_LastTestedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plans_LastReviewedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plans' AND BINARY INDEX_NAME = 'IX_incident_response_plans_LastReviewedById') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plans` RENAME INDEX `IX_IncidentResponsePlans_LastReviewedById` TO `IX_incident_response_plans_LastReviewedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plans_LastExercisedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plans' AND BINARY INDEX_NAME = 'IX_incident_response_plans_LastExercisedById') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plans` RENAME INDEX `IX_IncidentResponsePlans_LastExercisedById` TO `IX_incident_response_plans_LastExercisedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plans_CreatedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plans' AND BINARY INDEX_NAME = 'IX_incident_response_plans_CreatedById') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plans` RENAME INDEX `IX_IncidentResponsePlans_CreatedById` TO `IX_incident_response_plans_CreatedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plans_ApprovedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plans' AND BINARY INDEX_NAME = 'IX_incident_response_plans_ApprovedById') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plans` RENAME INDEX `IX_IncidentResponsePlans_ApprovedById` TO `IX_incident_response_plans_ApprovedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plan_executions_PlanId`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plan_executions' AND BINARY INDEX_NAME = 'IX_incident_response_plan_executions_PlanId') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plan_executions` RENAME INDEX `IX_IncidentResponsePlanExecutions_PlanId` TO `IX_incident_response_plan_executions_PlanId`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plan_executions_LastUpdatedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plan_executions' AND BINARY INDEX_NAME = 'IX_incident_response_plan_executions_LastUpdatedById') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plan_executions` RENAME INDEX `IX_IncidentResponsePlanExecutions_LastUpdatedById` TO `IX_incident_response_plan_executions_LastUpdatedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plan_executions_ExecutedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plan_executions' AND BINARY INDEX_NAME = 'IX_incident_response_plan_executions_ExecutedById') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plan_executions` RENAME INDEX `IX_IncidentResponsePlanExecutions_ExecutedById` TO `IX_incident_response_plan_executions_ExecutedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_incident_response_plan_executions_CreatedById`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_response_plan_executions' AND BINARY INDEX_NAME = 'IX_incident_response_plan_executions_CreatedById') > 0,
                 'DO 0', 'ALTER TABLE `incident_response_plan_executions` RENAME INDEX `IX_IncidentResponsePlanExecutions_CreatedById` TO `IX_incident_response_plan_executions_CreatedById`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_face_id_users_UserId`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'face_id_users' AND BINARY INDEX_NAME = 'IX_face_id_users_UserId') > 0,
                 'DO 0', 'ALTER TABLE `face_id_users` RENAME INDEX `IX_FaceIDUsers_UserId` TO `IX_face_id_users_UserId`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_face_id_users_LastUpdateUserId`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'face_id_users' AND BINARY INDEX_NAME = 'IX_face_id_users_LastUpdateUserId') > 0,
                 'DO 0', 'ALTER TABLE `face_id_users` RENAME INDEX `IX_FaceIDUsers_LastUpdateUserId` TO `IX_face_id_users_LastUpdateUserId`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_biometric_transactions_UserId`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'biometric_transactions' AND BINARY INDEX_NAME = 'IX_biometric_transactions_UserId') > 0,
                 'DO 0', 'ALTER TABLE `biometric_transactions` RENAME INDEX `IX_BiometricTransaction_UserId` TO `IX_biometric_transactions_UserId`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `IX_biometric_transactions_FaceIdUserId`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'biometric_transactions' AND BINARY INDEX_NAME = 'IX_biometric_transactions_FaceIdUserId') > 0,
                 'DO 0', 'ALTER TABLE `biometric_transactions` RENAME INDEX `IX_BiometricTransaction_FaceIdUserId` TO `IX_biometric_transactions_FaceIdUserId`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the primary key is already in place.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incident_to_incident_response_plan' AND BINARY INDEX_NAME = 'PRIMARY') > 0,
                 'DO 0', 'ALTER TABLE `incident_to_incident_response_plan` ADD CONSTRAINT `PK_incident_to_incident_response_plan` PRIMARY KEY (`IncidentId`, `IncidentResponsePlanId`)');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

ALTER TABLE `face_id_users` ADD CONSTRAINT `FK_face_id_users_user_UserId` FOREIGN KEY IF NOT EXISTS (`UserId`) REFERENCES `user` (`value`) ON DELETE CASCADE;

ALTER TABLE `incident_to_incident_response_plan` ADD CONSTRAINT `FK_incident_to_incident_response_plan_incident_response_plans_I~` FOREIGN KEY IF NOT EXISTS (`IncidentResponsePlanId`) REFERENCES `incident_response_plans` (`Id`) ON DELETE CASCADE;

ALTER TABLE `incident_to_incident_response_plan` ADD CONSTRAINT `FK_incident_to_incident_response_plan_incidents_IncidentId` FOREIGN KEY IF NOT EXISTS (`IncidentId`) REFERENCES `incidents` (`Id`) ON DELETE CASCADE;
