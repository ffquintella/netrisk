START TRANSACTION;

UPDATE user
SET login = CONVERT(username USING utf8);

update settings SET value = '55' where name = 'db_version';

COMMIT;