START TRANSACTION;

update settings SET value = '56' where name = 'db_version';

COMMIT;