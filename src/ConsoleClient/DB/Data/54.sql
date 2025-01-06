START TRANSACTION;

update settings SET value = '54' where name = 'db_version';

COMMIT;