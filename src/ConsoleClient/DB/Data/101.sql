START TRANSACTION;

-- Track 9 Stage 9.12 (M50, T211-T215, S53) -- AI governance: the model inventory, flag 11 and the
-- model metrics.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole.
--
-- Nothing is seeded or back-filled. The product runs no AI model, so the inventory starts empty; a
-- risk already declared with flag 11 keeps its declaration, and flag 11 is derived only from a link
-- to an inventoried model that someone makes (S53 D2).

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261008134540_Track9AiGovernance', '10.0.12')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

-- The write permission, granted to nobody: an administrator (the Admin role) holds it by construction,
-- everyone else receives it through a role. Reading the inventory needs no new permission -- it is the
-- risk register's audience, or this one. No explicit id: `key` is the unique natural key, so INSERT
-- IGNORE keyed on it is idempotent and collision-free (the pattern of Data/81.sql, SchemaUpgradeFilesTest).
INSERT IGNORE INTO `permissions` (`key`, `name`, `description`, `order`)
VALUES ('ai_governance_manage', 'Able to manage the AI model inventory',
        'Registers and retires AI models (purpose, data, vendor, version, risk tier, human oversight, owner), records their metric readings and the human overrides of their outputs, and voids a mistaken one. Governance only: nothing lets a model act, and every write needs the model''s own entity in scope.', 1);

update settings SET value = '101' where name = 'db_version';

COMMIT;
