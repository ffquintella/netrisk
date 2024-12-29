START TRANSACTION;

update settings SET value = '53' where name = 'db_version';

COMMIT;