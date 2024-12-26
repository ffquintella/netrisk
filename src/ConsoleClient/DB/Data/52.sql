START TRANSACTION;

update settings SET value = '52' where name = 'db_version';

COMMIT;