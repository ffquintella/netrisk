START TRANSACTION;

update settings SET value = '28' where name = 'db_version';

COMMIT;
