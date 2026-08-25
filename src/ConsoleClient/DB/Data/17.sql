START TRANSACTION;

update settings SET value = '17' where name = 'db_version';

COMMIT;
