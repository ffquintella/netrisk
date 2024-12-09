START TRANSACTION;


update settings SET value = '45' where name = 'db_version';

COMMIT;