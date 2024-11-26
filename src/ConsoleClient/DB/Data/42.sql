START TRANSACTION;

update settings SET value = '42' where name = 'db_version';

COMMIT;