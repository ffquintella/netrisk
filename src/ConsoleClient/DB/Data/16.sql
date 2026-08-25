START TRANSACTION;

update settings SET value = '16' where name = 'db_version';

COMMIT;
