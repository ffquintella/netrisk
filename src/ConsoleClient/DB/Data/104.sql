START TRANSACTION;

-- M52, T241 (S39 D-17; S40 section 11.1) -- the two permissions of the entity map.
--
-- Pure DML. A single CREATE or ALTER here would implicitly commit the transaction out from under the
-- rest of the script, and the db_version bump below would stop being the commit point that makes a
-- failed Data script roll back whole. There is no DDL in this version (Structure/104.sql is empty, as
-- 103 is) and no EF migration: the model does not change.
--
--   entities_manage      create, edit and delete entities (POST/PUT/DELETE /Entities). Until this
--                        version every authenticated user could; the gate is new.
--   entity_risk_context  set or change criticality, internetFacing and securityClassification -- the
--                        inputs that move the risk index's weights -- and only with global scope.
--
-- No explicit id: `key` is the unique natural key, so INSERT IGNORE keyed on it is idempotent and
-- collision-free (the pattern of Data/81.sql, SchemaUpgradeFilesTest).
INSERT IGNORE INTO `permissions` (`key`, `name`, `description`, `order`)
VALUES ('entities_manage', 'Able to create, edit and delete entities',
        'Creates, edits and deletes the entities of the business map (organization units, processes, applications and the rest). A caller with a restricted scope works only inside the subtree of the entities assigned to them; a root, or a move to the root, needs global scope.', 1),
       ('entity_risk_context', 'Able to set the risk context of entities',
        'Sets or changes the properties that move the risk index''s weights -- criticality, internet exposure and security classification -- on create and on edit. Requires global scope, because a process''s criticality moves every unit it serves; every change is in the field-level audit trail.', 1);

-- The grants, by role name and permission key -- never by a literal id, which differs between
-- installations. role_responsibilities is keyed (role_id, permission_id), so INSERT IGNORE makes a
-- second run grant nothing twice; a role an installation renamed or removed simply gets nothing.
--
--   Administrator  both. Data/1.sql gives it every permission it seeds, and its admin flag already passes
--                  every [PermissionAuthorize]; the rows keep that true for an installation that cleared
--                  the flag on it.
--   RiskAnalyst    entities_manage only. Every user could edit the map until now and analysts are the role
--                  that maintains it, so dropping it with the upgrade would lock them out of their own work.
--                  The risk-context inputs are not theirs by default: S40 section 11.1 gives them to the
--                  risk manager and risk administrator roles (T293), which an installation grants through the
--                  roles screen until those roles are seeded.
INSERT IGNORE INTO `role_responsibilities` (`role_id`, `permission_id`)
SELECT r.`value`, p.`id`
FROM `role` r
JOIN `permissions` p ON p.`key` IN ('entities_manage', 'entity_risk_context')
WHERE r.`name` = 'Administrator';

INSERT IGNORE INTO `role_responsibilities` (`role_id`, `permission_id`)
SELECT r.`value`, p.`id`
FROM `role` r
JOIN `permissions` p ON p.`key` = 'entities_manage'
WHERE r.`name` = 'RiskAnalyst';

update settings SET value = '104' where name = 'db_version';

COMMIT;
