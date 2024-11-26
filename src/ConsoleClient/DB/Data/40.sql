START TRANSACTION;

update settings SET value = '40' where name = 'db_version';

COMMIT;