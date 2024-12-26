START TRANSACTION;

update settings SET value = '51' where name = 'db_version';

COMMIT;