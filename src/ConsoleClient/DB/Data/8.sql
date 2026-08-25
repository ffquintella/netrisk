START TRANSACTION;

update settings SET value = '8' where name = 'db_version';

COMMIT;
