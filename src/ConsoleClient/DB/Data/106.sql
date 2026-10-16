START TRANSACTION;

-- M53, T243 and T293 (S39 §13.1; S40 §7.6, §11.1) -- the index permissions, the approver roles, and the
-- two index settings. Pure DML: a CREATE or ALTER here would commit the transaction out from under the
-- rest, and the db_version bump below would stop being the commit point. Every statement is keyed on a
-- natural key (permission key, role name, setting name), never a literal id, so a replay changes nothing.

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261016144648_RiskIndexTables', '10.0.13')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

-- T243: the three permissions. No explicit id: `key` is the unique natural key, so INSERT IGNORE keyed on
-- it is idempotent and collision-free (the pattern of Data/81.sql, SchemaUpgradeFilesTest).
INSERT IGNORE INTO `permissions` (`key`, `name`, `description`, `order`)
VALUES ('risk_index_view', 'Able to view the risk index',
        'Reads the Cyber Risk Overview dashboard and the risk index API, and the index profiles, and with unrestricted scope also their previews, bridges and history. Gives the value of the index, not the content of the records of other modules.', 1),
       ('risk_index_configure', 'Able to configure the risk index',
        'Edits a draft index profile, validates it, runs its sensitivity preview and submits it for approval, and edits the sources and annotations. Never approves.', 1),
       ('risk_index_approve', 'Able to approve risk index profiles',
        'Approves, rejects and suspends index profiles for the IT Risk Committee, attaches their evidence and publishes the index. Approval needs this permission explicitly, through an approver role: the administrator flag does not approve, and nobody approves a profile they wrote or edited.', 1);

-- T293: the two approver roles, created once. `role.name` has no unique index, so the guard is a NOT EXISTS
-- probe, as Data/98.sql does; a role an installation already created under this name is reused, not
-- duplicated. Neither is an administrator role.
INSERT INTO `role` (`name`, `admin`, `default`)
SELECT 'RiskManager', 0, NULL FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `role` WHERE `name` = 'RiskManager');

INSERT INTO `role` (`name`, `admin`, `default`)
SELECT 'RiskAdministrator', 0, NULL FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `role` WHERE `name` = 'RiskAdministrator');

-- The grants, by role name and permission key; `role_responsibilities` is keyed (role_id, permission_id), so
-- INSERT IGNORE grants nothing twice. The approver roles act for the committee and also set the entity risk
-- context (Data/104.sql); RiskAnalyst is the second-line author and never approves.
INSERT IGNORE INTO `role_responsibilities` (`role_id`, `permission_id`)
SELECT r.`value`, p.`id`
FROM `role` r
JOIN `permissions` p ON p.`key` IN ('risk_index_view', 'risk_index_configure', 'risk_index_approve', 'entity_risk_context')
WHERE r.`name` IN ('RiskManager', 'RiskAdministrator');

INSERT IGNORE INTO `role_responsibilities` (`role_id`, `permission_id`)
SELECT r.`value`, p.`id`
FROM `role` r
JOIN `permissions` p ON p.`key` IN ('risk_index_view', 'risk_index_configure')
WHERE r.`name` = 'RiskAnalyst';

-- Nothing is granted to Administrator, deliberately: approving weights is never implied by administering
-- the system (S40 §11.1, §11.4), and ThirdLineAuditor (Data/98.sql) is read-only by construction.

-- The two settings the approval and the shadow pilot read (S40 §6.4, §11.4). An operator's value is kept.
INSERT INTO `settings` (`name`, `value`) VALUES ('risk_index_approver_roles', 'RiskManager,RiskAdministrator')
ON DUPLICATE KEY UPDATE `value` = `value`;

INSERT INTO `settings` (`name`, `value`) VALUES ('risk_index_publication', 'shadow')
ON DUPLICATE KEY UPDATE `value` = `value`;

update settings SET value = '106' where name = 'db_version';

COMMIT;
