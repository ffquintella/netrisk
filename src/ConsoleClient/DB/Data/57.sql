START TRANSACTION;

update settings SET value = '57' where name = 'db_version';

COMMIT;