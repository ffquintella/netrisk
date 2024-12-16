START TRANSACTION;

update settings SET value = '48' where name = 'db_version';

COMMIT;