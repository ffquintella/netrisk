
ALTER TABLE `risks`
    ADD CONSTRAINT `fk_risk_category` FOREIGN KEY (`category`) REFERENCES `category` (`value`) ON DELETE RESTRICT,
    ADD CONSTRAINT `fk_risk_source` FOREIGN KEY (`source`) REFERENCES `source` (`value`) ON DELETE RESTRICT;

ALTER TABLE `risks`
    MODIFY COLUMN `source` int(11) NULL AFTER `control_number`,
    MODIFY COLUMN `category` int(11) NULL AFTER `source`;