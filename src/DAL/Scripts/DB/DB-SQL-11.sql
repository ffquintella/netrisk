ALTER TABLE `files`
    ADD COLUMN `mitigation_id` int NULL AFTER `content`,
    ADD INDEX `idx_risk_id`(`risk_id`) USING BTREE,
    ADD INDEX `idx_mitigation_id`(`mitigation_id`) USING BTREE;