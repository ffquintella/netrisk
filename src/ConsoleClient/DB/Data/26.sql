START TRANSACTION;

update settings SET value = '26' where name = 'db_version';

COMMIT;
