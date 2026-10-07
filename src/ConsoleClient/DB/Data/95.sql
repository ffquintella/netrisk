START TRANSACTION;

-- Track 9 Stage 9.6 (M44, T174-T181, S47) -- treatment economics.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole.
--
-- Nothing is seeded: no treatment option, monetary cost, dependency or target is invented by an
-- upgrade, and the planning_strategy labels are not mapped onto the typed option (S47 D2). The residual
-- mean is not back-filled either -- it only exists by recomputing the quantitative analysis, and until
-- then Gate C reports "residual mean not recorded" rather than reading the median (S47 R1). No
-- permission is created (the endpoints reuse RequireMitigation, RequirePlanMitigations and
-- RequireRiskmanagement, S47 D10).

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261007164200_Track9TreatmentEconomics', '10.0.12')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

update settings SET value = '95' where name = 'db_version';

COMMIT;
