START TRANSACTION;

INSERT INTO permissions (`key`, `name`, `description`,  `order`)
VALUES ('irp-exercise', 'Incident Response Plans - Can Exercise',  RPAD('', 65535, CHAR(7)), 17);

INSERT INTO permissions (`key`, `name`, `description`,  `order`)
VALUES ('irp-test', 'Incident Response Plans - Can test',  RPAD('', 65535, CHAR(7)), 18);

INSERT INTO permissions (`key`, `name`, `description`,  `order`)
VALUES ('irp-update', 'Incident Response Plans - Can update',  RPAD('', 65535, CHAR(7)), 19);

INSERT INTO permissions (`key`, `name`, `description`,  `order`)
VALUES ('irp-approve', 'Incident Response Plans - Can approve',  RPAD('', 65535, CHAR(7)), 20);

INSERT INTO permissions (`key`, `name`, `description`,  `order`)
VALUES ('irp-review', 'Incident Response Plans - Can approve',  RPAD('', 65535, CHAR(7)), 21);

update settings SET value = '44' where name = 'db_version';

COMMIT;