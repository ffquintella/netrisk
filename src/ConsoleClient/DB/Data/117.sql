START TRANSACTION;

-- M59, T288 (S39 sections 10.4 and 13.1) -- the third line reads the risk index in full mode through
-- M47's assurance role (T198): its profiles, sensitivity previews, bridges and their audit trail, with
-- global scope for any value. The read is risk_index_view; the role already holds third_line_assurance,
-- which makes every write of the API refuse it, so this grant opens no write path.
--
-- Pure DML. There is no DDL in this version (Structure/117.sql is empty, as 103 and 104 are) and no EF
-- migration: the model does not change. The grant is by role name and permission key, never by a
-- literal id; role_responsibilities is keyed (role_id, permission_id), so INSERT IGNORE makes a second
-- run grant nothing twice, and an installation that renamed or removed the role simply gets nothing.
INSERT IGNORE INTO `role_responsibilities` (`role_id`, `permission_id`)
SELECT r.`value`, p.`id`
FROM `role` r
JOIN `permissions` p ON p.`key` = 'risk_index_view'
WHERE r.`name` = 'ThirdLineAuditor';

update settings SET value = '117' where name = 'db_version';

COMMIT;
