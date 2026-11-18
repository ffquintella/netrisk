START TRANSACTION;

-- M58, T291 (S39 CMDB-INET / CMDB-REL). Pure DML: the db_version bump below is the commit point.
-- Nothing is backfilled: internet_facing waits for the next Jira Assets import whose mapping names the attribute, and the
-- relations for the next import whose mapping names an Applications or Hosts reference attribute.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261118180000_CmdbExposureAndApplicationLinks', '10.0.13')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '116' where name = 'db_version';

COMMIT;
