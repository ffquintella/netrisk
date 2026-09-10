START TRANSACTION;

-- Integration sync progress trail.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260910135746_AddIntegrationSyncProgressLog', '10.0.11')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '85' where name = 'db_version';

COMMIT;
