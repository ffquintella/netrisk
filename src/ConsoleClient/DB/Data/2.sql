START TRANSACTION;

update settings SET value = '2' where name = 'db_version';

COMMIT;
