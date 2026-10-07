START TRANSACTION;

-- Track 9 Stage 9.5 (M43, T168-T173, S46) -- the eleven mandatory flags and Gate A.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole.
--
-- Nothing is seeded: no flag is invented by an upgrade. The nightly RiskFlagsDerivation job derives
-- flags 3 (KEV), 4 (BIA) and 5 (data classification) on its first run; the rest are declared by an
-- assessor. No permission is created (the endpoints reuse RequireRiskmanagement and
-- RequireMgmtReviewAccess, S46 D15).

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261007154355_Track9RiskFlagsGateA', '10.0.12')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '94' where name = 'db_version';

COMMIT;
