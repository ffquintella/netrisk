START TRANSACTION;

INSERT INTO permissions (`key`, `name`, `description`,  `order`)
VALUES ('irp-delete', 'Incident Response Plans - Can delete',  RPAD('', 65535, CHAR(7)), 22);

update settings SET value = '46' where name = 'db_version';

COMMIT;