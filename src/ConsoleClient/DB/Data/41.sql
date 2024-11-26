START TRANSACTION;

update settings SET value = '41' where name = 'db_version';

COMMIT;