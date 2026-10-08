START TRANSACTION;

-- Track 9 Stage 9.9 (M47, T194-T199, S50) -- archival, backtesting, the risk committee and the third line.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole.
--
-- Nothing of the decision cycle is seeded: no archive, backtest, committee or member is invented by an
-- upgrade -- a committee is constituted by a Phase 0 act somebody has to record. What is seeded is the
-- third line (S50 section 4.7): the permission that makes its holder read-only, and a role carrying it
-- with the read permissions an internal auditor needs. Assigning the role to a person stays an
-- administrator's act.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261007200200_Track9DecisionCycle', '10.0.12')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

-- No explicit id: `permissions.id` is auto_increment and `key` is the unique column, so INSERT IGNORE
-- keyed on the natural key is idempotent and collision-free (the Data/81.sql pattern,
-- SchemaUpgradeFilesTest).
INSERT IGNORE INTO `permissions` (`key`, `name`, `description`, `order`)
VALUES ('third_line_assurance', 'Third line (internal audit): read-only assurance',
        'Marks the holder as the third line. A restriction, not a grant: every write of the API refuses a caller holding it, whatever other permission or administrator flag they also hold, and nobody holding it may be named an authorizing manager, a business reviewer or a committee member. Pair it with the read permissions the auditor needs.', 1);

-- The role, created once. `role.name` has no unique index, so the guard is a NOT EXISTS probe rather
-- than INSERT IGNORE. A role an administrator already created under this name is reused, not duplicated --
-- and receives the grants below, the marker included: the name is this release's.
INSERT INTO `role` (`name`, `admin`, `default`)
SELECT 'ThirdLineAuditor', 0, NULL FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `role` WHERE `name` = 'ThirdLineAuditor');

-- Its permissions: the marker and the reads (S50 D10). A key absent from this installation simply joins
-- no row. `role_responsibilities` is keyed on (role_id, permission_id), so INSERT IGNORE is idempotent.
INSERT IGNORE INTO `role_responsibilities` (`role_id`, `permission_id`)
SELECT r.`value`, p.`id`
FROM `role` r
JOIN `permissions` p ON p.`key` IN ('third_line_assurance', 'riskmanagement', 'governance', 'compliance',
                                    'assessments', 'reports', 'vulnerabilities', 'hosts', 'incident_management')
WHERE r.`name` = 'ThirdLineAuditor';

update settings SET value = '98' where name = 'db_version';

COMMIT;
