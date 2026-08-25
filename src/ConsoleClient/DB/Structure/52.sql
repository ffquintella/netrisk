-- Re-runnable by design. MariaDB implicitly commits every DDL statement, so wrapping this
-- script in a transaction would roll nothing back: a failure part-way through used to leave the
-- database between versions with no way out but hand-written SQL. Every statement below is
-- guarded instead, so applying this version again converges on the same schema — that, and not
-- a transaction, is what makes the upgrade safe to retry.

-- Guarded so re-running this version converges: the column is already named `Recommendations`.
SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.COLUMNS
                   WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'Incidents' AND BINARY COLUMN_NAME = 'Recommendations') > 0,
                 'DO 0', 'ALTER TABLE `Incidents` RENAME COLUMN `Recomendations` TO `Recommendations`');
PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20241226195742_RecommendationRename', '9.0.0')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

