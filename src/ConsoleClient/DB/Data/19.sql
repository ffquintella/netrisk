START TRANSACTION;

update settings SET value = '19' where name = 'db_version';

COMMIT;
