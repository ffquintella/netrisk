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