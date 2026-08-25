START TRANSACTION;

update settings SET value = '23' where name = 'db_version';

COMMIT;
