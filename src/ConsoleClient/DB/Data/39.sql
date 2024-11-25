START TRANSACTION;

update settings SET value = '39' where name = 'db_version';

COMMIT;