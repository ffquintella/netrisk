ALTER TABLE `files`
    ADD COLUMN `mitigation_id` int NULL AFTER `content`,
    ADD INDEX `idx_risk_id`(`risk_id`) USING BTREE,
    ADD INDEX `idx_mitigation_id`(`mitigation_id`) USING BTREE;

DELETE from mgmt_reviews WHERE risk_id not in (select id from risks);

ALTER TABLE `mgmt_reviews`
    ADD CONSTRAINT `fk_risk` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE;

ALTER TABLE `risks`
    DROP COLUMN `review_date`,
    DROP COLUMN `mgmt_review`;

update mgmt_reviews set reviewer = 2;

update mgmt_reviews set review = 1 where review=0;
insert into next_step values(4, 'Rejeitar');
update mgmt_reviews set next_step = 4 where next_step=0;

ALTER TABLE `mgmt_reviews`
    ADD CONSTRAINT `fw_rev` FOREIGN KEY (`reviewer`) REFERENCES `user` (`value`) ON DELETE RESTRICT ON UPDATE CASCADE,
    ADD CONSTRAINT `fk_review_type` FOREIGN KEY (`review`) REFERENCES `review` (`value`) ON DELETE RESTRICT ON UPDATE CASCADE,
    ADD CONSTRAINT `fk_next_step` FOREIGN KEY (`next_step`) REFERENCES `next_step` (`value`) ON DELETE RESTRICT ON UPDATE CASCADE;

update mgmt_reviews set next_review = '2023-01-01' where next_review='0000-00-00';