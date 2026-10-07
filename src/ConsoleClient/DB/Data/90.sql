START TRANSACTION;

-- Track 9, Stage 9.3 (S43, M41) -- business impact analysis, cascading dependencies and restoration
-- tests.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole.
--
-- Nothing is copied or back-filled: every existing process and service starts with no BIA -- absent,
-- which is the truth, and never RTO 0 nor infinite.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261007114838_Track9BusinessImpactAnalysis', '10.0.12')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

-- The two permissions, granted to nobody: an administrator (the Admin role) holds them by construction,
-- everyone else receives them through a role. No explicit id -- `key` is the unique natural key, so
-- INSERT IGNORE keyed on it is idempotent and collision-free (the pattern of Data/81.sql).
INSERT IGNORE INTO `permissions` (`key`, `name`, `description`, `order`)
VALUES ('bia_manage', 'Able to declare business impact analyses and continuity dependencies',
        'Declares MTPD/MAO, RTO and RPO on business processes and IT services, and the dependencies between them. These values set process criticality and the continuity basis of flag 4. Requires global scope.', 1),
       ('restoration_test_record', 'Able to record and void restoration tests',
        'Records the restoration tests that verify declared RTO/RPO, and voids a mistaken record with a reason. Records are never edited or deleted. Requires global scope.', 1);

-- The two Stage 9.3 parameters (S43 §4.7). ON DUPLICATE KEY UPDATE value = value: a retry never
-- overwrites a value an administrator has already changed.
INSERT INTO `settings` (`name`, `value`) VALUES ('continuity_restoration_test_validity_days', '365')
ON DUPLICATE KEY UPDATE `value` = `value`;
INSERT INTO `settings` (`name`, `value`) VALUES ('continuity_unverified_threat_weight', '0.5')
ON DUPLICATE KEY UPDATE `value` = `value`;

update settings SET value = '90' where name = 'db_version';

COMMIT;
