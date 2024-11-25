START TRANSACTION;

update settings SET value = '38' where name = 'db_version';

COMMIT;