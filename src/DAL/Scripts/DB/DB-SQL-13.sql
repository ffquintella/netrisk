ALTER TABLE `mitigations`
    DROP INDEX `risk_id`;


ALTER TABLE `mitigations`
    MODIFY COLUMN `mitigation_effort` int(11) NOT NULL DEFAULT 1 AFTER `planning_strategy`,
    MODIFY COLUMN `mitigation_cost` int(11) NOT NULL DEFAULT 1 AFTER `mitigation_effort`,
    MODIFY COLUMN `mitigation_owner` int(11) NOT NULL DEFAULT 1 AFTER `mitigation_cost`,
    MODIFY COLUMN `planning_strategy` int(11) NOT NULL DEFAULT 1 AFTER `last_update`;

update mitigations set mitigation_cost = 5 where mitigation_cost = 6;

ALTER TABLE mitigations
    ADD CONSTRAINT `fk_mitigation_cost` FOREIGN KEY (`mitigation_cost`) REFERENCES `mitigation_cost` (`value`) ON DELETE SET DEFAULT;

update mitigations set mitigation_effort = 1 where mitigation_effort = 0;

ALTER TABLE mitigations
    ADD CONSTRAINT `fk_mitigation_effort` FOREIGN KEY (`mitigation_effort`) REFERENCES `mitigation_effort` (`value`) ON DELETE SET DEFAULT;

update mitigations set mitigation_owner = 2 where mitigation_owner not in (select value from `user`) ;

ALTER TABLE mitigations ADD CONSTRAINT `fk_mitigation_owner` FOREIGN KEY (`mitigation_owner`) REFERENCES `user` (`value`) ON DELETE RESTRICT;

update mitigations set planning_strategy = 1 where planning_strategy not in (select value from `planning_strategy`) ;

ALTER TABLE mitigations ADD CONSTRAINT `fk_planning_strategy` FOREIGN KEY (`planning_strategy`) REFERENCES `planning_strategy` (`value`) ON DELETE RESTRICT;

update mitigations set submitted_by = 2 where submitted_by not in (select value from `user`) ;

ALTER TABLE mitigations ADD CONSTRAINT `fk_submitted_by` FOREIGN KEY (`submitted_by`) REFERENCES `user` (`value`) ON DELETE RESTRICT;

delete from mitigations where risk_id not in (select id from risks) ;

ALTER TABLE mitigations ADD CONSTRAINT `fk_risks` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE;


update risks set mitigation_id = null where mitigation_id not in (select id from mitigations);

ALTER TABLE `risks`
    ADD CONSTRAINT `fk_risk_mitigation` FOREIGN KEY (`mitigation_id`) REFERENCES `mitigations` (`id`) ON DELETE SET NULL;
