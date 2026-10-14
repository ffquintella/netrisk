START TRANSACTION;

-- T237 review (M52): the EF history row of the report-schedule creator column (Structure/105.sql) and the version bump.
--
-- Pure DML. No permission is seeded: the report-schedule endpoints reuse `reports` (Data/1.sql, id 43, "Able to access
-- reports"), which every role that reaches the Reports screen already holds, and an administrator passes every
-- [PermissionAuthorize] through the admin flag.
INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261013120000_ReportScheduleCreator', '10.0.12')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '105' where name = 'db_version';

COMMIT;
