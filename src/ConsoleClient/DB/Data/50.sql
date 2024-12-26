START TRANSACTION;

update settings SET value = '50' where name = 'db_version';

COMMIT;