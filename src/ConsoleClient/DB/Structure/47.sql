-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Guarded so re-running this version converges: the index is already named `idx_irpt_status1`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'IncidentResponsePlanTasks' AND BINARY INDEX_NAME = 'idx_irpt_status1') > 0,
                 'DO 0', 'ALTER TABLE `IncidentResponsePlanTasks` RENAME INDEX `idx_irpt_status2` TO `idx_irpt_status1`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `idx_irp_status`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'IncidentResponsePlans' AND BINARY INDEX_NAME = 'idx_irp_status') > 0,
                 'DO 0', 'ALTER TABLE `IncidentResponsePlans` RENAME INDEX `idx_irpt_status` TO `idx_irp_status`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

-- Guarded so re-running this version converges: the index is already named `idx_irpt_status`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'IncidentResponsePlanExecutions' AND BINARY INDEX_NAME = 'idx_irpt_status') > 0,
                 'DO 0', 'ALTER TABLE `IncidentResponsePlanExecutions` RENAME INDEX `idx_irpt_status1` TO `idx_irpt_status`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

ALTER TABLE `IncidentResponsePlanTasks` ADD COLUMN IF NOT EXISTS `Name` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT '';

CREATE INDEX IF NOT EXISTS `idx_irpt_exec_order` ON `IncidentResponsePlanTasks` (`ExecutionOrder`);

CREATE FULLTEXT INDEX IF NOT EXISTS `idx_irpt_name` ON `IncidentResponsePlanTasks` (`Name`);

CREATE INDEX IF NOT EXISTS `idx_irpt_optinal` ON `IncidentResponsePlanTasks` (`IsOptional`);

CREATE INDEX IF NOT EXISTS `idx_irpt_parallel` ON `IncidentResponsePlanTasks` (`IsParallel`);

CREATE INDEX IF NOT EXISTS `idx_irpt_priority` ON `IncidentResponsePlanTasks` (`Priority`);

CREATE INDEX IF NOT EXISTS `idx_irpt_sequencial` ON `IncidentResponsePlanTasks` (`IsSequential`);

CREATE INDEX IF NOT EXISTS `idx_irp_approved` ON `IncidentResponsePlans` (`HasBeenApproved`);

CREATE INDEX IF NOT EXISTS `idx_irp_lupdate` ON `IncidentResponsePlans` (`LastUpdate`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241216164130_IRPTName', '9.0.0')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

