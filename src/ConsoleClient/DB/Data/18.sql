START TRANSACTION;

update settings SET value = '18' where name = 'db_version';

COMMIT;
