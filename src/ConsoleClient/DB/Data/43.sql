START TRANSACTION;

update settings SET value = '43' where name = 'db_version';

COMMIT;