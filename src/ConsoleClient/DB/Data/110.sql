START TRANSACTION;

-- M55, T264 (S40 §6.7, §8.5) -- the risk-context notes start empty: a change made before this version went through the
-- entity form, which records no justification, and its audit rows read back without one. Pure DML: a CREATE or ALTER
-- here would commit the transaction out from under the rest, and the db_version bump below would stop being the commit
-- point.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261118110526_EntityRiskContextNotes', '10.0.13')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '110' where name = 'db_version';

COMMIT;
