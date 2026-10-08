START TRANSACTION;

-- Track 9 Stage 9.11 (M49, T206-T210, S52) -- the LGPD data catalogue.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole.
--
-- Nothing is copied or back-filled. The register had no free-text legal requirement to migrate:
-- risks.regulation was an id into a dead lookup table, dropped at version 73; risks.control_number is
-- a framework-control number the statistics group by, not a legal requirement, and stays as it is
-- (S52 D8). Every data record starts uncatalogued, which the catalogue reports as a finding rather
-- than as "no personal data" (S52 D4).

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261008120830_Track9DataCatalogue', '10.0.12')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

-- The write permission, granted to nobody: an administrator (the Admin role) holds it by construction,
-- everyone else receives it through a role. Reading the catalogue needs no new permission -- it is the
-- risk register's audience, or this one. No explicit id: `key` is the unique natural key, so INSERT
-- IGNORE keyed on it is idempotent and collision-free (the pattern of Data/81.sql, SchemaUpgradeFilesTest).
INSERT IGNORE INTO `permissions` (`key`, `name`, `description`, `order`)
VALUES ('data_catalogue_manage', 'Able to manage the LGPD data catalogue',
        'Catalogues data records (personal-data category, purposes with their legal basis, retention, location and international transfer), maintains the legal and contractual requirements, and writes, approves and retires RIPDs (DPIAs). The catalogue describes kinds of data, never a data subject''s values. Every write needs global scope.', 1);

update settings SET value = '100' where name = 'db_version';

COMMIT;
