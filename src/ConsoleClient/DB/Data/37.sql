START TRANSACTION;

update settings SET value = '37' where name = 'db_version';

COMMIT;