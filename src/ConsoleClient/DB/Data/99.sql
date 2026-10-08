START TRANSACTION;

-- Track 9 Stage 9.10 (M48, T200-T205, S51) -- the third-party register.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole.
--
-- Nothing is copied or back-filled: a supplier somebody modelled as a generic `organization` node is
-- not converted, because nothing tells a supplier from the main organization or from any other
-- organization node (S51 section 3, negative scope). Every supplier starts absent from the register,
-- which is the truth.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261008110025_Track9ThirdParties', '10.0.12')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

-- The write permission, granted to nobody: an administrator (the Admin role) holds it by construction,
-- everyone else receives it through a role. Reading the register needs no new permission -- it is the
-- risk register's audience, or this one. No explicit id: `key` is the unique natural key, so INSERT
-- IGNORE keyed on it is idempotent and collision-free (the pattern of Data/81.sql, SchemaUpgradeFilesTest).
INSERT IGNORE INTO `permissions` (`key`, `name`, `description`, `order`)
VALUES ('third_party_manage', 'Able to manage the third-party register',
        'Registers suppliers, clouds and identity providers with their contract, SLA, contracted RTO/RPO, right to audit and exit plan; links them to the IT services, processes and data records they supply or process; declares sub-processors and data locations; records HECVAT assessments and imports SBOMs. A supplier of an entity needs that entity in scope; the organization''s suppliers need global scope.', 1);

update settings SET value = '99' where name = 'db_version';

COMMIT;
