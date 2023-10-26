INSERT INTO `close_reason` (`value`, `name`) VALUES (0, 'Rejected');
INSERT INTO `close_reason` (`value`, `name`) VALUES (1, 'Fully Mitigated');
INSERT INTO `close_reason` (`value`, `name`) VALUES (2, 'Activity not donne anymore');
INSERT INTO `close_reason` (`value`, `name`) VALUES (3, 'Belongs to third party');
INSERT INTO `close_reason` (`value`, `name`) VALUES (4, 'Not relevant');


update settings SET value = '11' where name = 'db_version';