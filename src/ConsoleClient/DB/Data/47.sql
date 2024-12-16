START TRANSACTION;

update settings SET value = '47' where name = 'db_version';

COMMIT;