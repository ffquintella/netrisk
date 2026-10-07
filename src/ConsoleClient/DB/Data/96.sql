START TRANSACTION;

-- Track 9 Stage 9.7 (M45, T182-T187, S48) -- tail statistics and portfolio.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole.
--
-- Nothing is seeded: no loss component, correlation or appetite tolerance is invented by an upgrade.
-- The tail statistics are not back-filled either -- they exist only by simulating, so they appear on
-- the first recomputation (the 02:20 residual job, or a manual computation), and until then Gate B
-- on the tail reports "no tail statistics" rather than "within tolerance" (S48 R1). The two flag 8
-- thresholds (tail_flag_max_annual_probability, tail_flag_catastrophic_loss) have defaults in code and
-- are not seeded. No permission is created (the endpoints reuse RequireRiskmanagement,
-- RequireSubmitRisk and RequireAdminOnly, S48 D11).

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261007173517_Track9TailRisk', '10.0.12')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '96' where name = 'db_version';

COMMIT;
