START TRANSACTION;

-- Track 9 Stage 9.4 (M42, T162-T167, S45) -- exploitation signals.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole.
--
-- Nothing is seeded: the KEV catalogue and the EPSS readings arrive with the first synchronization, no
-- permission is created (the endpoints reuse vulnerabilities, vulnerabilities_create and
-- RequireRiskmanagement), and no existing finding is given an EPSS it was never measured with.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261007142310_Track9ExploitationSignals', '10.0.12')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '93' where name = 'db_version';

COMMIT;
