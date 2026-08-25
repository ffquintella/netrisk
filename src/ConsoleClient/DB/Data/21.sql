START TRANSACTION;

update settings SET value = '21' where name = 'db_version';

COMMIT;
