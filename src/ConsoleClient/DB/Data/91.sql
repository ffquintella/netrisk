START TRANSACTION;

-- GitHub #79 -- the server/application classification of a vulnerability.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole.
--
-- Nothing is back-filled: an existing finding was never classified, so it stays Unknown (the column
-- default) until somebody classifies it.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261007123306_AddVulnerabilitySourceType', '10.0.12')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '91' where name = 'db_version';

COMMIT;
