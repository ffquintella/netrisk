START TRANSACTION;

-- M55, T263 (S40 §6.6, §7.2) -- the rules start empty and host_external_ids is filled by the next Vision One sync of a
-- connection with sync_custom_tags on (the S40 §7.6 copy of hosts.external_id waits for the identity chain, T267/T275).
-- Pure DML: a CREATE or ALTER here would commit the transaction out from under the rest, and the db_version bump below
-- would stop being the commit point.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261118131212_VendorEntityMappings', '10.0.13')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '111' where name = 'db_version';

COMMIT;
