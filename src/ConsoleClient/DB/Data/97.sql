START TRANSACTION;

-- Track 9 Stage 9.8 (M46, T188-T193, S49) -- KRIs, reassessment triggers and the methodology's metrics.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole.
--
-- Nothing is seeded: no indicator, tolerance or reassessment event is invented by an upgrade -- a
-- tolerance is a Phase 0 decision somebody has to record with its rationale. No setting is needed (the
-- evaluation job's time is in code) and no permission is created (the endpoints reuse
-- RequireRiskmanagement, RequireSubmitRisk and RequireAdminOnly, S49 D11).

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261007182320_Track9KriReassessment', '10.0.12')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '97' where name = 'db_version';

COMMIT;
