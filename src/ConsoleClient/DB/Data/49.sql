START TRANSACTION;

INSERT INTO permissions (`key`, `name`, `description`,  `order`)
VALUES ('incident_management', 'Incident Management Access',  RPAD('', 65535, CHAR(7)), 17);

update settings SET value = '49' where name = 'db_version';

COMMIT;