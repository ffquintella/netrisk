START TRANSACTION;

-- Track 4 milestone 4.6 -- Jira Service Management and Assets.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260902192253_Track46JiraServiceManagementAndAssets', '10.0.11')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

-- Every link that predates 4.6 is a finding link. The column default already says so for rows
-- written from now on; this states it for the rows that were there when the column was added, in
-- case a server on a different version wrote one between the Structure script and this one.
UPDATE `finding_issue_links`
SET `target_kind` = 1
WHERE `target_kind` IS NULL OR `target_kind` = 0;

update settings SET value = '83' where name = 'db_version';

COMMIT;
