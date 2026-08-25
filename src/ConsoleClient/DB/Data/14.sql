START TRANSACTION;

update settings SET value = '14' where name = 'db_version';

COMMIT;
