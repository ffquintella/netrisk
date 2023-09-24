/*
 
 NetRisk Base DB Structure

 Date: 24/09/2023 11:16:29
*/

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- ----------------------------
-- Table structure for api_keys
-- ----------------------------
DROP TABLE IF EXISTS `api_keys`;
CREATE TABLE `api_keys`  (
                             `id` int(6) UNSIGNED NOT NULL AUTO_INCREMENT,
                             `name` varchar(30) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                             `value` varchar(50) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                             `status` varchar(10) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                             `creation_date` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE CURRENT_TIMESTAMP,
                             `client_ip` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                             PRIMARY KEY (`id`) USING BTREE,
                             UNIQUE INDEX `idx_api_keys_value`(`value`) USING BTREE,
                             INDEX `idx_api_keys_ip`(`client_ip`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 10 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for assessment_answers
-- ----------------------------
DROP TABLE IF EXISTS `assessment_answers`;
CREATE TABLE `assessment_answers`  (
                                       `id` int(11) NOT NULL AUTO_INCREMENT,
                                       `assessment_id` int(11) NOT NULL,
                                       `question_id` int(11) NOT NULL,
                                       `answer` varchar(200) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                       `submit_risk` tinyint(1) NOT NULL DEFAULT 0,
                                       `risk_subject` blob NOT NULL,
                                       `risk_score` float NOT NULL,
                                       `assessment_scoring_id` int(11) NOT NULL,
                                       `risk_owner` int(11) NULL DEFAULT NULL,
                                       `order` int(11) NOT NULL DEFAULT 999999,
                                       PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1283 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for assessment_answers_to_asset_groups
-- ----------------------------
DROP TABLE IF EXISTS `assessment_answers_to_asset_groups`;
CREATE TABLE `assessment_answers_to_asset_groups`  (
                                                       `assessment_answer_id` int(11) NOT NULL,
                                                       `asset_group_id` int(11) NOT NULL,
                                                       UNIQUE INDEX `assessment_answer_asset_group_unique`(`assessment_answer_id`, `asset_group_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for assessment_answers_to_assets
-- ----------------------------
DROP TABLE IF EXISTS `assessment_answers_to_assets`;
CREATE TABLE `assessment_answers_to_assets`  (
                                                 `assessment_answer_id` int(11) NOT NULL,
                                                 `asset_id` int(11) NOT NULL,
                                                 UNIQUE INDEX `assessment_answer_asset_unique`(`assessment_answer_id`, `asset_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for assessment_questions
-- ----------------------------
DROP TABLE IF EXISTS `assessment_questions`;
CREATE TABLE `assessment_questions`  (
                                         `id` int(11) NOT NULL AUTO_INCREMENT,
                                         `assessment_id` int(11) NOT NULL,
                                         `question` varchar(1000) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                         `order` int(11) NOT NULL DEFAULT 999999,
                                         PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 642 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for assessment_scoring
-- ----------------------------
DROP TABLE IF EXISTS `assessment_scoring`;
CREATE TABLE `assessment_scoring`  (
                                       `id` int(11) NOT NULL AUTO_INCREMENT,
                                       `scoring_method` int(11) NOT NULL,
                                       `calculated_risk` float NOT NULL,
                                       `CLASSIC_likelihood` float NOT NULL DEFAULT 5,
                                       `CLASSIC_impact` float NOT NULL DEFAULT 5,
                                       `CVSS_AccessVector` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'N',
                                       `CVSS_AccessComplexity` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'L',
                                       `CVSS_Authentication` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'N',
                                       `CVSS_ConfImpact` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'C',
                                       `CVSS_IntegImpact` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'C',
                                       `CVSS_AvailImpact` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'C',
                                       `CVSS_Exploitability` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'ND',
                                       `CVSS_RemediationLevel` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'ND',
                                       `CVSS_ReportConfidence` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'ND',
                                       `CVSS_CollateralDamagePotential` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'ND',
                                       `CVSS_TargetDistribution` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'ND',
                                       `CVSS_ConfidentialityRequirement` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'ND',
                                       `CVSS_IntegrityRequirement` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'ND',
                                       `CVSS_AvailabilityRequirement` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'ND',
                                       `DREAD_DamagePotential` int(11) NULL DEFAULT 10,
                                       `DREAD_Reproducibility` int(11) NULL DEFAULT 10,
                                       `DREAD_Exploitability` int(11) NULL DEFAULT 10,
                                       `DREAD_AffectedUsers` int(11) NULL DEFAULT 10,
                                       `DREAD_Discoverability` int(11) NULL DEFAULT 10,
                                       `OWASP_SkillLevel` int(11) NULL DEFAULT 10,
                                       `OWASP_Motive` int(11) NULL DEFAULT 10,
                                       `OWASP_Opportunity` int(11) NULL DEFAULT 10,
                                       `OWASP_Size` int(11) NULL DEFAULT 10,
                                       `OWASP_EaseOfDiscovery` int(11) NULL DEFAULT 10,
                                       `OWASP_EaseOfExploit` int(11) NULL DEFAULT 10,
                                       `OWASP_Awareness` int(11) NULL DEFAULT 10,
                                       `OWASP_IntrusionDetection` int(11) NULL DEFAULT 10,
                                       `OWASP_LossOfConfidentiality` int(11) NULL DEFAULT 10,
                                       `OWASP_LossOfIntegrity` int(11) NULL DEFAULT 10,
                                       `OWASP_LossOfAvailability` int(11) NULL DEFAULT 10,
                                       `OWASP_LossOfAccountability` int(11) NULL DEFAULT 10,
                                       `OWASP_FinancialDamage` int(11) NULL DEFAULT 10,
                                       `OWASP_ReputationDamage` int(11) NULL DEFAULT 10,
                                       `OWASP_NonCompliance` int(11) NULL DEFAULT 10,
                                       `OWASP_PrivacyViolation` int(11) NULL DEFAULT 10,
                                       `Custom` float NULL DEFAULT 10,
                                       `Contributing_Likelihood` int(11) NULL DEFAULT 0,
                                       PRIMARY KEY (`id`) USING BTREE,
                                       UNIQUE INDEX `id`(`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1281 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for assessment_scoring_contributing_impacts
-- ----------------------------
DROP TABLE IF EXISTS `assessment_scoring_contributing_impacts`;
CREATE TABLE `assessment_scoring_contributing_impacts`  (
                                                            `id` int(11) NOT NULL AUTO_INCREMENT,
                                                            `assessment_scoring_id` int(11) NOT NULL,
                                                            `contributing_risk_id` int(11) NOT NULL,
                                                            `impact` int(11) NOT NULL,
                                                            PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for assessments
-- ----------------------------
DROP TABLE IF EXISTS `assessments`;
CREATE TABLE `assessments`  (
                                `id` int(11) NOT NULL AUTO_INCREMENT,
                                `name` varchar(200) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                `created` timestamp NOT NULL DEFAULT current_timestamp(),
                                PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 6 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for audit_log
-- ----------------------------
DROP TABLE IF EXISTS `audit_log`;
CREATE TABLE `audit_log`  (
                              `timestamp` timestamp NOT NULL DEFAULT current_timestamp(),
                              `risk_id` int(11) NOT NULL,
                              `user_id` int(11) NOT NULL DEFAULT 0,
                              `message` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                              `log_type` varchar(100) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for category
-- ----------------------------
DROP TABLE IF EXISTS `category`;
CREATE TABLE `category`  (
                             `value` int(11) NOT NULL AUTO_INCREMENT,
                             `name` varchar(50) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                             PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 9 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for client_registration
-- ----------------------------
DROP TABLE IF EXISTS `client_registration`;
CREATE TABLE `client_registration`  (
                                        `Id` int(11) NOT NULL AUTO_INCREMENT,
                                        `Name` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                                        `ExternalId` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                        `Hostname` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                                        `LoggedAccount` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                                        `RegistrationDate` datetime NOT NULL DEFAULT '0000-00-00 00:00:00' ON UPDATE CURRENT_TIMESTAMP,
                                        `LastVerificationDate` datetime NOT NULL DEFAULT '0000-00-00 00:00:00' ON UPDATE CURRENT_TIMESTAMP,
                                        `Status` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'requested',
                                        PRIMARY KEY (`Id`) USING BTREE,
                                        INDEX `ExternalId`(`ExternalId`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 9 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for close_reason
-- ----------------------------
DROP TABLE IF EXISTS `close_reason`;
CREATE TABLE `close_reason`  (
                                 `value` int(11) NOT NULL AUTO_INCREMENT,
                                 `name` varchar(50) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                 PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 12 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for closures
-- ----------------------------
DROP TABLE IF EXISTS `closures`;
CREATE TABLE `closures`  (
                             `id` int(11) NOT NULL AUTO_INCREMENT,
                             `risk_id` int(11) NOT NULL,
                             `user_id` int(11) NOT NULL,
                             `closure_date` timestamp NOT NULL DEFAULT current_timestamp(),
                             `close_reason` int(11) NOT NULL,
                             `note` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                             PRIMARY KEY (`id`) USING BTREE,
                             INDEX `closures_close_reason_idx`(`close_reason`) USING BTREE,
                             INDEX `closures_user_id_idx`(`user_id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 143 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for comments
-- ----------------------------
DROP TABLE IF EXISTS `comments`;
CREATE TABLE `comments`  (
                             `id` int(11) NOT NULL AUTO_INCREMENT,
                             `risk_id` int(11) NOT NULL,
                             `date` timestamp NOT NULL DEFAULT current_timestamp(),
                             `user` int(11) NOT NULL,
                             `comment` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                             PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 596 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for contributing_risks
-- ----------------------------
DROP TABLE IF EXISTS `contributing_risks`;
CREATE TABLE `contributing_risks`  (
                                       `id` int(11) NOT NULL AUTO_INCREMENT,
                                       `subject` varchar(1000) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                       `weight` float NOT NULL,
                                       PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 5 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for contributing_risks_impact
-- ----------------------------
DROP TABLE IF EXISTS `contributing_risks_impact`;
CREATE TABLE `contributing_risks_impact`  (
                                              `id` int(11) NOT NULL AUTO_INCREMENT,
                                              `contributing_risks_id` int(11) NOT NULL,
                                              `value` int(11) NOT NULL,
                                              `name` varchar(100) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                              PRIMARY KEY (`id`) USING BTREE,
                                              INDEX `contributing_risks_id`(`contributing_risks_id`) USING BTREE,
                                              INDEX `cri_index`(`contributing_risks_id`, `value`) USING BTREE,
                                              INDEX `cri_index2`(`value`, `contributing_risks_id`) USING BTREE,
                                              INDEX `cri_value_idx`(`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 21 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for contributing_risks_likelihood
-- ----------------------------
DROP TABLE IF EXISTS `contributing_risks_likelihood`;
CREATE TABLE `contributing_risks_likelihood`  (
                                                  `id` int(11) NOT NULL AUTO_INCREMENT,
                                                  `value` int(11) NOT NULL,
                                                  `name` varchar(100) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                                  PRIMARY KEY (`id`) USING BTREE,
                                                  INDEX `crl_index`(`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 6 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for control_class
-- ----------------------------
DROP TABLE IF EXISTS `control_class`;
CREATE TABLE `control_class`  (
                                  `value` int(11) NOT NULL AUTO_INCREMENT,
                                  `name` mediumtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                  PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 4 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for control_maturity
-- ----------------------------
DROP TABLE IF EXISTS `control_maturity`;
CREATE TABLE `control_maturity`  (
                                     `value` int(11) NOT NULL,
                                     `name` mediumtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                     PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for control_phase
-- ----------------------------
DROP TABLE IF EXISTS `control_phase`;
CREATE TABLE `control_phase`  (
                                  `value` int(11) NOT NULL AUTO_INCREMENT,
                                  `name` mediumtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                  PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 5 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for control_priority
-- ----------------------------
DROP TABLE IF EXISTS `control_priority`;
CREATE TABLE `control_priority`  (
                                     `value` int(11) NOT NULL AUTO_INCREMENT,
                                     `name` mediumtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                     PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 15 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for control_type
-- ----------------------------
DROP TABLE IF EXISTS `control_type`;
CREATE TABLE `control_type`  (
                                 `value` int(11) NOT NULL AUTO_INCREMENT,
                                 `name` mediumtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                 PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 4 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for custom_risk_model_values
-- ----------------------------
DROP TABLE IF EXISTS `custom_risk_model_values`;
CREATE TABLE `custom_risk_model_values`  (
                                             `impact` int(11) NOT NULL,
                                             `likelihood` int(11) NOT NULL,
                                             `value` double(3, 1) NOT NULL,
                                             UNIQUE INDEX `impact_likelihood_unique`(`impact`, `likelihood`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for entities
-- ----------------------------
DROP TABLE IF EXISTS `entities`;
CREATE TABLE `entities`  (
                             `Id` int(11) NOT NULL AUTO_INCREMENT,
                             `DefinitionName` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
                             `DefinitionVersion` varchar(15) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
                             `Created` timestamp NOT NULL DEFAULT current_timestamp(),
                             `Updated` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE CURRENT_TIMESTAMP,
                             `CreatedBy` int(11) NOT NULL,
                             `UpdatedBy` int(11) NOT NULL,
                             `Status` varchar(15) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
                             `Parent` int(11) NULL DEFAULT NULL,
                             PRIMARY KEY (`Id`) USING BTREE,
                             INDEX `idx_definition_name`(`DefinitionName`) USING BTREE,
                             INDEX `fk_parent`(`Parent`) USING BTREE,
                             CONSTRAINT `fk_parent` FOREIGN KEY (`Parent`) REFERENCES `entities` (`Id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE = InnoDB AUTO_INCREMENT = 63 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for entities_properties
-- ----------------------------
DROP TABLE IF EXISTS `entities_properties`;
CREATE TABLE `entities_properties`  (
                                        `Id` int(11) NOT NULL AUTO_INCREMENT,
                                        `Type` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
                                        `Value` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
                                        `OldValue` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
                                        `Entity` int(11) NOT NULL,
                                        `Name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
                                        PRIMARY KEY (`Id`) USING BTREE,
                                        INDEX `idx_name`(`Name`) USING BTREE,
                                        INDEX `fk_entity`(`Entity`) USING BTREE,
                                        FULLTEXT INDEX `idx_value`(`Value`),
                                        CONSTRAINT `fk_entity` FOREIGN KEY (`Entity`) REFERENCES `entities` (`Id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE = InnoDB AUTO_INCREMENT = 174 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for failed_login_attempts
-- ----------------------------
DROP TABLE IF EXISTS `failed_login_attempts`;
CREATE TABLE `failed_login_attempts`  (
                                          `id` int(11) NOT NULL AUTO_INCREMENT,
                                          `expired` tinyint(4) NULL DEFAULT 0,
                                          `user_id` int(11) NOT NULL,
                                          `ip` varchar(15) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT '0.0.0.0',
                                          `date` timestamp NOT NULL DEFAULT current_timestamp(),
                                          PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for family
-- ----------------------------
DROP TABLE IF EXISTS `family`;
CREATE TABLE `family`  (
                           `value` int(11) NOT NULL AUTO_INCREMENT,
                           `name` varchar(100) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                           PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 23 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for file_type_extensions
-- ----------------------------
DROP TABLE IF EXISTS `file_type_extensions`;
CREATE TABLE `file_type_extensions`  (
                                         `value` int(11) NOT NULL AUTO_INCREMENT,
                                         `name` varchar(10) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                         PRIMARY KEY (`value`) USING BTREE,
                                         UNIQUE INDEX `name`(`name`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 23 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for file_types
-- ----------------------------
DROP TABLE IF EXISTS `file_types`;
CREATE TABLE `file_types`  (
                               `value` int(11) NOT NULL AUTO_INCREMENT,
                               `name` varchar(250) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                               PRIMARY KEY (`value`) USING BTREE,
                               UNIQUE INDEX `name`(`name`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 26 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for files
-- ----------------------------
DROP TABLE IF EXISTS `files`;
CREATE TABLE `files`  (
                          `id` int(11) NOT NULL AUTO_INCREMENT,
                          `risk_id` int(11) NULL DEFAULT 0,
                          `view_type` int(11) NULL DEFAULT 1,
                          `name` varchar(100) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                          `unique_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
                          `type` varchar(128) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                          `size` int(11) NOT NULL,
                          `timestamp` timestamp NOT NULL DEFAULT current_timestamp(),
                          `user` int(11) NOT NULL,
                          `content` longblob NOT NULL,
                          `mitigation_id` int(11) NULL DEFAULT NULL,
                          PRIMARY KEY (`id`) USING BTREE,
                          INDEX `idx_risk_id`(`risk_id`) USING BTREE,
                          INDEX `idx_mitigation_id`(`mitigation_id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 103 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for framework_control_mappings
-- ----------------------------
DROP TABLE IF EXISTS `framework_control_mappings`;
CREATE TABLE `framework_control_mappings`  (
                                               `id` int(11) NOT NULL AUTO_INCREMENT,
                                               `control_id` int(11) NOT NULL,
                                               `framework` int(11) NOT NULL,
                                               `reference_name` varchar(200) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                               PRIMARY KEY (`id`) USING BTREE,
                                               INDEX `control_id`(`control_id`) USING BTREE,
                                               INDEX `framework`(`framework`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 425 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for framework_control_test_audits
-- ----------------------------
DROP TABLE IF EXISTS `framework_control_test_audits`;
CREATE TABLE `framework_control_test_audits`  (
                                                  `id` int(11) NOT NULL AUTO_INCREMENT,
                                                  `test_id` int(11) NOT NULL,
                                                  `tester` int(11) NOT NULL,
                                                  `test_frequency` int(11) NOT NULL DEFAULT 0,
                                                  `last_date` date NOT NULL,
                                                  `next_date` date NOT NULL,
                                                  `name` mediumtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                                  `objective` mediumtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                                  `test_steps` mediumtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                                  `approximate_time` int(11) NOT NULL,
                                                  `expected_results` mediumtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                                  `framework_control_id` int(11) NOT NULL,
                                                  `desired_frequency` int(11) NULL DEFAULT NULL,
                                                  `status` int(11) NOT NULL DEFAULT 1,
                                                  `created_at` datetime NOT NULL,
                                                  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for framework_control_test_comments
-- ----------------------------
DROP TABLE IF EXISTS `framework_control_test_comments`;
CREATE TABLE `framework_control_test_comments`  (
                                                    `id` int(11) NOT NULL AUTO_INCREMENT,
                                                    `test_audit_id` int(11) NOT NULL,
                                                    `date` timestamp NOT NULL DEFAULT current_timestamp(),
                                                    `user` int(11) NOT NULL,
                                                    `comment` mediumtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                                    PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for framework_control_test_results
-- ----------------------------
DROP TABLE IF EXISTS `framework_control_test_results`;
CREATE TABLE `framework_control_test_results`  (
                                                   `id` int(11) NOT NULL AUTO_INCREMENT,
                                                   `test_audit_id` int(11) NOT NULL,
                                                   `test_result` varchar(50) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                                   `summary` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                                   `test_date` date NOT NULL,
                                                   `submitted_by` int(11) NOT NULL,
                                                   `submission_date` datetime NOT NULL,
                                                   `last_updated` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE CURRENT_TIMESTAMP,
                                                   PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for framework_control_test_results_to_risks
-- ----------------------------
DROP TABLE IF EXISTS `framework_control_test_results_to_risks`;
CREATE TABLE `framework_control_test_results_to_risks`  (
                                                            `id` int(11) NOT NULL AUTO_INCREMENT,
                                                            `test_results_id` int(11) NULL DEFAULT NULL,
                                                            `risk_id` int(11) NULL DEFAULT NULL,
                                                            PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for framework_control_tests
-- ----------------------------
DROP TABLE IF EXISTS `framework_control_tests`;
CREATE TABLE `framework_control_tests`  (
                                            `id` int(11) NOT NULL AUTO_INCREMENT,
                                            `tester` int(11) NOT NULL,
                                            `test_frequency` int(11) NOT NULL DEFAULT 0,
                                            `last_date` date NOT NULL,
                                            `next_date` date NOT NULL,
                                            `name` mediumtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                            `objective` mediumtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                            `test_steps` mediumtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                            `approximate_time` int(11) NOT NULL,
                                            `expected_results` mediumtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                            `framework_control_id` int(11) NOT NULL,
                                            `desired_frequency` int(11) NULL DEFAULT NULL,
                                            `status` int(11) NOT NULL DEFAULT 1,
                                            `created_at` date NULL DEFAULT NULL,
                                            `additional_stakeholders` varchar(500) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                            PRIMARY KEY (`id`) USING BTREE,
                                            UNIQUE INDEX `id`(`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for framework_control_to_framework
-- ----------------------------
DROP TABLE IF EXISTS `framework_control_to_framework`;
CREATE TABLE `framework_control_to_framework`  (
                                                   `control_id` int(11) NOT NULL,
                                                   `framework_id` int(11) NOT NULL,
                                                   PRIMARY KEY (`control_id`, `framework_id`) USING BTREE,
                                                   INDEX `framework_id`(`framework_id`, `control_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for framework_control_type_mappings
-- ----------------------------
DROP TABLE IF EXISTS `framework_control_type_mappings`;
CREATE TABLE `framework_control_type_mappings`  (
                                                    `id` int(11) NOT NULL AUTO_INCREMENT,
                                                    `control_id` int(11) NOT NULL,
                                                    `control_type_id` int(11) NOT NULL,
                                                    PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 208 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for framework_controls
-- ----------------------------
DROP TABLE IF EXISTS `framework_controls`;
CREATE TABLE `framework_controls`  (
                                       `id` int(11) NOT NULL AUTO_INCREMENT,
                                       `short_name` varchar(1000) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                                       `long_name` blob NULL DEFAULT NULL,
                                       `description` blob NULL DEFAULT NULL,
                                       `supplemental_guidance` blob NULL DEFAULT NULL,
                                       `control_owner` int(11) NULL DEFAULT NULL,
                                       `control_class` int(11) NULL DEFAULT NULL,
                                       `control_phase` int(11) NULL DEFAULT NULL,
                                       `control_number` varchar(100) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                                       `control_maturity` int(11) NOT NULL DEFAULT 0,
                                       `desired_maturity` int(11) NOT NULL DEFAULT 0,
                                       `control_priority` int(11) NULL DEFAULT NULL,
                                       `control_status` tinyint(1) NULL DEFAULT 1,
                                       `family` int(11) NULL DEFAULT NULL,
                                       `submission_date` timestamp NOT NULL DEFAULT current_timestamp(),
                                       `last_audit_date` date NULL DEFAULT NULL,
                                       `next_audit_date` date NULL DEFAULT NULL,
                                       `desired_frequency` int(11) NULL DEFAULT NULL,
                                       `mitigation_percent` int(11) NOT NULL DEFAULT 0,
                                       `status` int(11) NOT NULL DEFAULT 1,
                                       `deleted` tinyint(4) NOT NULL DEFAULT 0,
                                       PRIMARY KEY (`id`) USING BTREE,
                                       INDEX `framework_controls_deleted_idx`(`deleted`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 55 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for frameworks
-- ----------------------------
DROP TABLE IF EXISTS `frameworks`;
CREATE TABLE `frameworks`  (
                               `value` int(11) NOT NULL AUTO_INCREMENT,
                               `parent` int(11) NOT NULL,
                               `name` blob NOT NULL,
                               `description` blob NOT NULL,
                               `status` int(11) NOT NULL DEFAULT 1,
                               `order` int(11) NOT NULL,
                               `last_audit_date` date NULL DEFAULT NULL,
                               `next_audit_date` date NULL DEFAULT NULL,
                               `desired_frequency` int(11) NULL DEFAULT NULL,
                               PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 16 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for impact
-- ----------------------------
DROP TABLE IF EXISTS `impact`;
CREATE TABLE `impact`  (
                           `name` varchar(50) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                           `value` int(11) NOT NULL,
                           INDEX `impact_index`(`value`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for likelihood
-- ----------------------------
DROP TABLE IF EXISTS `likelihood`;
CREATE TABLE `likelihood`  (
                               `name` varchar(50) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                               `value` int(11) NOT NULL,
                               INDEX `likelihood_index`(`value`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for links
-- ----------------------------
DROP TABLE IF EXISTS `links`;
CREATE TABLE `links`  (
                          `id` int(11) NOT NULL AUTO_INCREMENT,
                          `key_hash` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
                          `type` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
                          `creation_date` datetime NOT NULL DEFAULT current_timestamp(),
                          `expiration_date` datetime NULL DEFAULT NULL,
                          `data` blob NULL DEFAULT NULL,
                          PRIMARY KEY (`id`) USING BTREE,
                          UNIQUE INDEX `key_type_idx`(`key_hash`, `type`) USING BTREE,
                          INDEX `expiration_date_idx`(`expiration_date`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 9 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for location
-- ----------------------------
DROP TABLE IF EXISTS `location`;
CREATE TABLE `location`  (
                             `value` int(11) NOT NULL AUTO_INCREMENT,
                             `name` varchar(100) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                             PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 19 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for mgmt_reviews
-- ----------------------------
DROP TABLE IF EXISTS `mgmt_reviews`;
CREATE TABLE `mgmt_reviews`  (
                                 `id` int(11) NOT NULL AUTO_INCREMENT,
                                 `risk_id` int(11) NOT NULL,
                                 `submission_date` timestamp NOT NULL DEFAULT current_timestamp(),
                                 `review` int(11) NOT NULL,
                                 `reviewer` int(11) NOT NULL,
                                 `next_step` int(11) NOT NULL,
                                 `comments` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                 `next_review` date NOT NULL DEFAULT '0000-00-00',
                                 PRIMARY KEY (`id`) USING BTREE,
                                 INDEX `fk_risk`(`risk_id`) USING BTREE,
                                 INDEX `fw_rev`(`reviewer`) USING BTREE,
                                 INDEX `fk_review_type`(`review`) USING BTREE,
                                 INDEX `fk_next_step`(`next_step`) USING BTREE,
                                 CONSTRAINT `fk_next_step` FOREIGN KEY (`next_step`) REFERENCES `next_step` (`value`) ON DELETE RESTRICT ON UPDATE CASCADE,
                                 CONSTRAINT `fk_review_type` FOREIGN KEY (`review`) REFERENCES `review` (`value`) ON DELETE RESTRICT ON UPDATE CASCADE,
                                 CONSTRAINT `fk_risk` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE ON UPDATE RESTRICT,
                                 CONSTRAINT `fw_rev` FOREIGN KEY (`reviewer`) REFERENCES `user` (`value`) ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE = InnoDB AUTO_INCREMENT = 424 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for mitigation_accept_users
-- ----------------------------
DROP TABLE IF EXISTS `mitigation_accept_users`;
CREATE TABLE `mitigation_accept_users`  (
                                            `id` int(11) NOT NULL AUTO_INCREMENT,
                                            `risk_id` int(11) NOT NULL,
                                            `user_id` int(11) NOT NULL,
                                            `created_at` datetime NOT NULL,
                                            PRIMARY KEY (`id`) USING BTREE,
                                            INDEX `mau_risk_id_idx`(`risk_id`) USING BTREE,
                                            INDEX `mau_user_idx`(`user_id`) USING BTREE,
                                            INDEX `mau_user_risk_idx`(`user_id`, `risk_id`) USING BTREE,
                                            INDEX `mau_risk_user_idx`(`risk_id`, `user_id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 11 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for mitigation_cost
-- ----------------------------
DROP TABLE IF EXISTS `mitigation_cost`;
CREATE TABLE `mitigation_cost`  (
                                    `value` int(11) NOT NULL AUTO_INCREMENT,
                                    `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
                                    PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 6 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for mitigation_effort
-- ----------------------------
DROP TABLE IF EXISTS `mitigation_effort`;
CREATE TABLE `mitigation_effort`  (
                                      `value` int(11) NOT NULL AUTO_INCREMENT,
                                      `name` varchar(20) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                      PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 6 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for mitigation_to_controls
-- ----------------------------
DROP TABLE IF EXISTS `mitigation_to_controls`;
CREATE TABLE `mitigation_to_controls`  (
                                           `mitigation_id` int(11) NOT NULL,
                                           `control_id` int(11) NOT NULL,
                                           `validation_details` mediumtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                                           `validation_owner` int(11) NULL DEFAULT 0,
                                           `validation_mitigation_percent` int(11) NULL DEFAULT 0,
                                           PRIMARY KEY (`mitigation_id`, `control_id`) USING BTREE,
                                           INDEX `control_id`(`control_id`, `mitigation_id`) USING BTREE,
                                           INDEX `mtg2ctrl_mtg_idx`(`mitigation_id`) USING BTREE,
                                           INDEX `mtg2ctrl_control_idx`(`control_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for mitigation_to_team
-- ----------------------------
DROP TABLE IF EXISTS `mitigation_to_team`;
CREATE TABLE `mitigation_to_team`  (
                                       `mitigation_id` int(11) NOT NULL,
                                       `team_id` int(11) NOT NULL,
                                       PRIMARY KEY (`mitigation_id`, `team_id`) USING BTREE,
                                       INDEX `team_id`(`team_id`, `mitigation_id`) USING BTREE,
                                       INDEX `mtg2team_mtg_id`(`mitigation_id`) USING BTREE,
                                       INDEX `mtg2team_team_id`(`team_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for mitigations
-- ----------------------------
DROP TABLE IF EXISTS `mitigations`;
CREATE TABLE `mitigations`  (
                                `id` int(11) NOT NULL AUTO_INCREMENT,
                                `risk_id` int(11) NOT NULL,
                                `submission_date` timestamp NOT NULL DEFAULT current_timestamp(),
                                `last_update` timestamp NOT NULL DEFAULT '0000-00-00 00:00:00',
                                `planning_strategy` int(11) NOT NULL DEFAULT 1,
                                `mitigation_effort` int(11) NOT NULL DEFAULT 1,
                                `mitigation_cost` int(11) NOT NULL DEFAULT 1,
                                `mitigation_owner` int(11) NOT NULL DEFAULT 1,
                                `current_solution` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                `security_requirements` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                `security_recommendations` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                `submitted_by` int(11) NOT NULL DEFAULT 1,
                                `planning_date` date NOT NULL,
                                `mitigation_percent` int(11) NOT NULL,
                                PRIMARY KEY (`id`) USING BTREE,
                                INDEX `fk_mitigation_cost`(`mitigation_cost`) USING BTREE,
                                INDEX `fk_mitigation_effort`(`mitigation_effort`) USING BTREE,
                                INDEX `fk_mitigation_owner`(`mitigation_owner`) USING BTREE,
                                INDEX `fk_planning_strategy`(`planning_strategy`) USING BTREE,
                                INDEX `fk_risks`(`risk_id`) USING BTREE,
                                INDEX `fk_submitted_by`(`submitted_by`) USING BTREE,
                                CONSTRAINT `fk_mitigation_cost` FOREIGN KEY (`mitigation_cost`) REFERENCES `mitigation_cost` (`value`) ON DELETE RESTRICT ON UPDATE RESTRICT,
                                CONSTRAINT `fk_mitigation_effort` FOREIGN KEY (`mitigation_effort`) REFERENCES `mitigation_effort` (`value`) ON DELETE RESTRICT ON UPDATE RESTRICT,
                                CONSTRAINT `fk_mitigation_owner` FOREIGN KEY (`mitigation_owner`) REFERENCES `user` (`value`) ON DELETE RESTRICT ON UPDATE RESTRICT,
                                CONSTRAINT `fk_planning_strategy` FOREIGN KEY (`planning_strategy`) REFERENCES `planning_strategy` (`value`) ON DELETE RESTRICT ON UPDATE RESTRICT,
                                CONSTRAINT `fk_risks` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE ON UPDATE RESTRICT,
                                CONSTRAINT `fk_submitted_by` FOREIGN KEY (`submitted_by`) REFERENCES `user` (`value`) ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE = InnoDB AUTO_INCREMENT = 269 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for next_step
-- ----------------------------
DROP TABLE IF EXISTS `next_step`;
CREATE TABLE `next_step`  (
                              `value` int(11) NOT NULL AUTO_INCREMENT,
                              `name` varchar(50) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                              PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 5 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for pending_risks
-- ----------------------------
DROP TABLE IF EXISTS `pending_risks`;
CREATE TABLE `pending_risks`  (
                                  `id` int(11) NOT NULL AUTO_INCREMENT,
                                  `assessment_id` int(11) NOT NULL,
                                  `assessment_answer_id` int(11) NOT NULL,
                                  `subject` blob NOT NULL,
                                  `score` float NOT NULL,
                                  `owner` int(11) NULL DEFAULT NULL,
                                  `affected_assets` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                                  `comment` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                  `submission_date` timestamp NOT NULL DEFAULT current_timestamp(),
                                  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for permission_groups
-- ----------------------------
DROP TABLE IF EXISTS `permission_groups`;
CREATE TABLE `permission_groups`  (
                                      `id` int(11) NOT NULL AUTO_INCREMENT,
                                      `name` varchar(200) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                      `description` blob NOT NULL,
                                      `order` int(11) NOT NULL,
                                      PRIMARY KEY (`id`) USING BTREE,
                                      UNIQUE INDEX `name`(`name`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 6 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for permission_to_permission_group
-- ----------------------------
DROP TABLE IF EXISTS `permission_to_permission_group`;
CREATE TABLE `permission_to_permission_group`  (
                                                   `permission_id` int(11) NOT NULL,
                                                   `permission_group_id` int(11) NOT NULL,
                                                   PRIMARY KEY (`permission_id`, `permission_group_id`) USING BTREE,
                                                   INDEX `permission_group_id`(`permission_group_id`, `permission_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for permission_to_user
-- ----------------------------
DROP TABLE IF EXISTS `permission_to_user`;
CREATE TABLE `permission_to_user`  (
                                       `permission_id` int(11) NOT NULL,
                                       `user_id` int(11) NOT NULL,
                                       PRIMARY KEY (`permission_id`, `user_id`) USING BTREE,
                                       INDEX `user_id`(`user_id`, `permission_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for permissions
-- ----------------------------
DROP TABLE IF EXISTS `permissions`;
CREATE TABLE `permissions`  (
                                `id` int(11) NOT NULL AUTO_INCREMENT,
                                `key` varchar(100) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                `name` varchar(200) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                `description` blob NOT NULL,
                                `order` int(11) NOT NULL,
                                PRIMARY KEY (`id`) USING BTREE,
                                UNIQUE INDEX `key`(`key`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 44 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for planning_strategy
-- ----------------------------
DROP TABLE IF EXISTS `planning_strategy`;
CREATE TABLE `planning_strategy`  (
                                      `value` int(11) NOT NULL AUTO_INCREMENT,
                                      `name` varchar(20) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                      PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 7 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for questionnaire_pending_risks
-- ----------------------------
DROP TABLE IF EXISTS `questionnaire_pending_risks`;
CREATE TABLE `questionnaire_pending_risks`  (
                                                `id` int(11) NOT NULL AUTO_INCREMENT,
                                                `questionnaire_tracking_id` int(11) NOT NULL,
                                                `questionnaire_scoring_id` int(11) NOT NULL,
                                                `subject` blob NOT NULL,
                                                `owner` int(11) NULL DEFAULT NULL,
                                                `asset` varchar(200) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                                                `comment` varchar(500) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                                                `submission_date` timestamp NOT NULL DEFAULT current_timestamp(),
                                                PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for regulation
-- ----------------------------
DROP TABLE IF EXISTS `regulation`;
CREATE TABLE `regulation`  (
                               `value` int(11) NOT NULL AUTO_INCREMENT,
                               `name` varchar(50) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                               PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 5 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for residual_risk_scoring_history
-- ----------------------------
DROP TABLE IF EXISTS `residual_risk_scoring_history`;
CREATE TABLE `residual_risk_scoring_history`  (
                                                  `id` int(11) NOT NULL AUTO_INCREMENT,
                                                  `risk_id` int(11) NOT NULL,
                                                  `residual_risk` float NOT NULL,
                                                  `last_update` datetime NOT NULL,
                                                  PRIMARY KEY (`id`) USING BTREE,
                                                  INDEX `risk_id`(`risk_id`) USING BTREE,
                                                  INDEX `rrsh_last_update_idx`(`last_update`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 401 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for review
-- ----------------------------
DROP TABLE IF EXISTS `review`;
CREATE TABLE `review`  (
                           `value` int(11) NOT NULL AUTO_INCREMENT,
                           `name` varchar(50) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                           PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 4 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for review_levels
-- ----------------------------
DROP TABLE IF EXISTS `review_levels`;
CREATE TABLE `review_levels`  (
                                  `id` int(11) NOT NULL DEFAULT 0,
                                  `value` int(11) NOT NULL,
                                  `name` varchar(20) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for risk_catalog
-- ----------------------------
DROP TABLE IF EXISTS `risk_catalog`;
CREATE TABLE `risk_catalog`  (
                                 `id` int(11) NOT NULL AUTO_INCREMENT,
                                 `number` varchar(20) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                 `grouping` int(11) NOT NULL,
                                 `name` varchar(1000) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                 `description` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                 `function` int(11) NOT NULL,
                                 `order` int(11) NOT NULL,
                                 PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 37 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for risk_function
-- ----------------------------
DROP TABLE IF EXISTS `risk_function`;
CREATE TABLE `risk_function`  (
                                  `value` int(11) NOT NULL AUTO_INCREMENT,
                                  `name` varchar(50) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                  PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 6 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for risk_grouping
-- ----------------------------
DROP TABLE IF EXISTS `risk_grouping`;
CREATE TABLE `risk_grouping`  (
                                  `value` int(11) NOT NULL AUTO_INCREMENT,
                                  `name` varchar(50) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                  `default` tinyint(1) NOT NULL DEFAULT 0,
                                  `order` int(11) NOT NULL,
                                  PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 9 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for risk_levels
-- ----------------------------
DROP TABLE IF EXISTS `risk_levels`;
CREATE TABLE `risk_levels`  (
                                `value` decimal(3, 1) NOT NULL,
                                `name` varchar(20) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                `color` varchar(20) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                `display_name` varchar(20) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                INDEX `risk_levels_value_idx`(`value`) USING BTREE,
                                INDEX `risk_levels_name_idx`(`name`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;


-- ----------------------------
-- Table structure for risk_scoring
-- ----------------------------
DROP TABLE IF EXISTS `risk_scoring`;
CREATE TABLE `risk_scoring`  (
                                 `id` int(11) NOT NULL,
                                 `scoring_method` int(11) NOT NULL,
                                 `calculated_risk` float NOT NULL,
                                 `CLASSIC_likelihood` float NOT NULL DEFAULT 5,
                                 `CLASSIC_impact` float NOT NULL DEFAULT 5,
                                 `CVSS_AccessVector` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'N',
                                 `CVSS_AccessComplexity` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'L',
                                 `CVSS_Authentication` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'N',
                                 `CVSS_ConfImpact` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'C',
                                 `CVSS_IntegImpact` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'C',
                                 `CVSS_AvailImpact` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'C',
                                 `CVSS_Exploitability` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'ND',
                                 `CVSS_RemediationLevel` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'ND',
                                 `CVSS_ReportConfidence` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'ND',
                                 `CVSS_CollateralDamagePotential` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'ND',
                                 `CVSS_TargetDistribution` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'ND',
                                 `CVSS_ConfidentialityRequirement` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'ND',
                                 `CVSS_IntegrityRequirement` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'ND',
                                 `CVSS_AvailabilityRequirement` varchar(3) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'ND',
                                 `DREAD_DamagePotential` int(11) NULL DEFAULT 10,
                                 `DREAD_Reproducibility` int(11) NULL DEFAULT 10,
                                 `DREAD_Exploitability` int(11) NULL DEFAULT 10,
                                 `DREAD_AffectedUsers` int(11) NULL DEFAULT 10,
                                 `DREAD_Discoverability` int(11) NULL DEFAULT 10,
                                 `OWASP_SkillLevel` int(11) NULL DEFAULT 10,
                                 `OWASP_Motive` int(11) NULL DEFAULT 10,
                                 `OWASP_Opportunity` int(11) NULL DEFAULT 10,
                                 `OWASP_Size` int(11) NULL DEFAULT 10,
                                 `OWASP_EaseOfDiscovery` int(11) NULL DEFAULT 10,
                                 `OWASP_EaseOfExploit` int(11) NULL DEFAULT 10,
                                 `OWASP_Awareness` int(11) NULL DEFAULT 10,
                                 `OWASP_IntrusionDetection` int(11) NULL DEFAULT 10,
                                 `OWASP_LossOfConfidentiality` int(11) NULL DEFAULT 10,
                                 `OWASP_LossOfIntegrity` int(11) NULL DEFAULT 10,
                                 `OWASP_LossOfAvailability` int(11) NULL DEFAULT 10,
                                 `OWASP_LossOfAccountability` int(11) NULL DEFAULT 10,
                                 `OWASP_FinancialDamage` int(11) NULL DEFAULT 10,
                                 `OWASP_ReputationDamage` int(11) NULL DEFAULT 10,
                                 `OWASP_NonCompliance` int(11) NULL DEFAULT 10,
                                 `OWASP_PrivacyViolation` int(11) NULL DEFAULT 10,
                                 `Custom` float NULL DEFAULT 10,
                                 `Contributing_Likelihood` int(11) NULL DEFAULT 0,
                                 PRIMARY KEY (`id`) USING BTREE,
                                 UNIQUE INDEX `id`(`id`) USING BTREE,
                                 INDEX `calculated_risk`(`calculated_risk`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for risk_scoring_contributing_impacts
-- ----------------------------
DROP TABLE IF EXISTS `risk_scoring_contributing_impacts`;
CREATE TABLE `risk_scoring_contributing_impacts`  (
                                                      `id` int(11) NOT NULL AUTO_INCREMENT,
                                                      `risk_scoring_id` int(11) NOT NULL,
                                                      `contributing_risk_id` int(11) NOT NULL,
                                                      `impact` int(11) NOT NULL,
                                                      PRIMARY KEY (`id`) USING BTREE,
                                                      INDEX `risk_scoring_id`(`risk_scoring_id`) USING BTREE,
                                                      INDEX `contributing_risk_id`(`contributing_risk_id`) USING BTREE,
                                                      INDEX `rsci_index`(`risk_scoring_id`, `contributing_risk_id`) USING BTREE,
                                                      INDEX `rsci_index2`(`contributing_risk_id`, `risk_scoring_id`) USING BTREE,
                                                      INDEX `rsci_impact_idx`(`impact`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for risk_scoring_history
-- ----------------------------
DROP TABLE IF EXISTS `risk_scoring_history`;
CREATE TABLE `risk_scoring_history`  (
                                         `id` int(11) NOT NULL AUTO_INCREMENT,
                                         `risk_id` int(11) NOT NULL,
                                         `calculated_risk` float NOT NULL,
                                         `last_update` datetime NOT NULL,
                                         PRIMARY KEY (`id`) USING BTREE,
                                         INDEX `risk_id`(`risk_id`) USING BTREE,
                                         INDEX `rsh_last_update_idx`(`last_update`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 419 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for risk_to_additional_stakeholder
-- ----------------------------
DROP TABLE IF EXISTS `risk_to_additional_stakeholder`;
CREATE TABLE `risk_to_additional_stakeholder`  (
                                                   `risk_id` int(11) NOT NULL,
                                                   `user_id` int(11) NOT NULL,
                                                   PRIMARY KEY (`risk_id`, `user_id`) USING BTREE,
                                                   INDEX `user_id`(`user_id`, `risk_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for risk_to_entity
-- ----------------------------
DROP TABLE IF EXISTS `risk_to_entity`;
CREATE TABLE `risk_to_entity`  (
                                   `risk_id` int(11) NOT NULL,
                                   `entity_id` int(11) NOT NULL,
                                   PRIMARY KEY (`risk_id`, `entity_id`) USING BTREE,
                                   INDEX `fk_entity_id`(`entity_id`) USING BTREE,
                                   CONSTRAINT `fk_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE ON UPDATE NO ACTION,
                                   CONSTRAINT `fk_risk_id` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for risk_to_location
-- ----------------------------
DROP TABLE IF EXISTS `risk_to_location`;
CREATE TABLE `risk_to_location`  (
                                     `risk_id` int(11) NOT NULL,
                                     `location_id` int(11) NOT NULL,
                                     PRIMARY KEY (`risk_id`, `location_id`) USING BTREE,
                                     INDEX `location_id`(`location_id`, `risk_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for risk_to_team
-- ----------------------------
DROP TABLE IF EXISTS `risk_to_team`;
CREATE TABLE `risk_to_team`  (
                                 `risk_id` int(11) NOT NULL,
                                 `team_id` int(11) NOT NULL,
                                 PRIMARY KEY (`risk_id`, `team_id`) USING BTREE,
                                 INDEX `team_id`(`team_id`, `risk_id`) USING BTREE,
                                 INDEX `risk2team_risk_id`(`risk_id`) USING BTREE,
                                 INDEX `risk2team_team_id`(`team_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for risk_to_technology
-- ----------------------------
DROP TABLE IF EXISTS `risk_to_technology`;
CREATE TABLE `risk_to_technology`  (
                                       `risk_id` int(11) NOT NULL,
                                       `technology_id` int(11) NOT NULL,
                                       PRIMARY KEY (`risk_id`, `technology_id`) USING BTREE,
                                       INDEX `technology_id`(`technology_id`, `risk_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for risks
-- ----------------------------
DROP TABLE IF EXISTS `risks`;
CREATE TABLE `risks`  (
                          `id` int(11) NOT NULL AUTO_INCREMENT,
                          `status` varchar(20) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                          `subject` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                          `reference_id` varchar(20) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT '',
                          `regulation` int(11) NULL DEFAULT NULL,
                          `control_number` varchar(20) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                          `source` int(11) NOT NULL,
                          `category` int(11) NOT NULL,
                          `owner` int(11) NOT NULL,
                          `manager` int(11) NOT NULL,
                          `assessment` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                          `notes` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                          `submission_date` timestamp NOT NULL DEFAULT current_timestamp(),
                          `last_update` timestamp NOT NULL DEFAULT '0000-00-00 00:00:00',
                          `mitigation_id` int(11) NULL DEFAULT 0,
                          `project_id` int(11) NOT NULL DEFAULT 0,
                          `close_id` int(11) NULL DEFAULT NULL,
                          `submitted_by` int(11) NOT NULL DEFAULT 1,
                          `risk_catalog_mapping` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                          `threat_catalog_mapping` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                          `template_group_id` int(11) NOT NULL DEFAULT 1,
                          PRIMARY KEY (`id`) USING BTREE,
                          INDEX `category`(`category`) USING BTREE,
                          INDEX `close_id`(`close_id`) USING BTREE,
                          INDEX `manager`(`manager`) USING BTREE,
                          INDEX `owner`(`owner`) USING BTREE,
                          INDEX `project_id`(`project_id`) USING BTREE,
                          INDEX `source`(`source`) USING BTREE,
                          INDEX `status`(`status`) USING BTREE,
                          INDEX `submitted_by`(`submitted_by`) USING BTREE,
                          INDEX `regulation`(`regulation`) USING BTREE,
                          INDEX `fk_risk_mitigation`(`mitigation_id`) USING BTREE,
                          CONSTRAINT `fk_risk_mitigation` FOREIGN KEY (`mitigation_id`) REFERENCES `mitigations` (`id`) ON DELETE SET NULL ON UPDATE RESTRICT
) ENGINE = InnoDB AUTO_INCREMENT = 333 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for risks_to_asset_groups
-- ----------------------------
DROP TABLE IF EXISTS `risks_to_asset_groups`;
CREATE TABLE `risks_to_asset_groups`  (
                                          `risk_id` int(11) NOT NULL,
                                          `asset_group_id` int(11) NOT NULL,
                                          UNIQUE INDEX `risk_asset_group_unique`(`risk_id`, `asset_group_id`) USING BTREE,
                                          INDEX `asset_group_id`(`asset_group_id`, `risk_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for risks_to_assets
-- ----------------------------
DROP TABLE IF EXISTS `risks_to_assets`;
CREATE TABLE `risks_to_assets`  (
                                    `risk_id` int(11) NULL DEFAULT NULL,
                                    `asset_id` int(11) NOT NULL,
                                    UNIQUE INDEX `risk_id`(`risk_id`, `asset_id`) USING BTREE,
                                    INDEX `asset_id`(`asset_id`, `risk_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for role
-- ----------------------------
DROP TABLE IF EXISTS `role`;
CREATE TABLE `role`  (
                         `value` int(11) NOT NULL AUTO_INCREMENT,
                         `name` varchar(100) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                         `admin` tinyint(1) NOT NULL DEFAULT 0,
                         `default` tinyint(1) NULL DEFAULT NULL,
                         PRIMARY KEY (`value`) USING BTREE,
                         UNIQUE INDEX `default`(`default`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 4 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for role_responsibilities
-- ----------------------------
DROP TABLE IF EXISTS `role_responsibilities`;
CREATE TABLE `role_responsibilities`  (
                                          `role_id` int(11) NOT NULL,
                                          `permission_id` int(11) NOT NULL,
                                          PRIMARY KEY (`role_id`, `permission_id`) USING BTREE,
                                          INDEX `permission_id`(`permission_id`, `role_id`) USING BTREE,
                                          CONSTRAINT `fk_role_p_id` FOREIGN KEY (`role_id`) REFERENCES `role` (`value`) ON DELETE CASCADE ON UPDATE CASCADE,
                                          CONSTRAINT `fk_role_perm_id` FOREIGN KEY (`permission_id`) REFERENCES `permissions` (`id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for scoring_methods
-- ----------------------------
DROP TABLE IF EXISTS `scoring_methods`;
CREATE TABLE `scoring_methods`  (
                                    `value` int(11) NOT NULL AUTO_INCREMENT,
                                    `name` varchar(20) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                                    PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 7 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for settings
-- ----------------------------
DROP TABLE IF EXISTS `settings`;
CREATE TABLE `settings`  (
                             `name` varchar(100) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT '',
                             `value` mediumtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                             PRIMARY KEY (`name`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for source
-- ----------------------------
DROP TABLE IF EXISTS `source`;
CREATE TABLE `source`  (
                           `value` int(11) NOT NULL AUTO_INCREMENT,
                           `name` varchar(50) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                           PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 6 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for status
-- ----------------------------
DROP TABLE IF EXISTS `status`;
CREATE TABLE `status`  (
                           `value` int(11) NOT NULL AUTO_INCREMENT,
                           `name` varchar(50) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                           PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 8 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for tags
-- ----------------------------
DROP TABLE IF EXISTS `tags`;
CREATE TABLE `tags`  (
                         `id` int(11) NOT NULL AUTO_INCREMENT,
                         `tag` varchar(500) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                         PRIMARY KEY (`id`) USING BTREE,
                         UNIQUE INDEX `tag_unique`(`tag`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 253 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for tags_taggees
-- ----------------------------
DROP TABLE IF EXISTS `tags_taggees`;
CREATE TABLE `tags_taggees`  (
                                 `tag_id` int(11) NOT NULL,
                                 `taggee_id` int(11) NOT NULL,
                                 `type` varchar(40) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                                 UNIQUE INDEX `tag_taggee_unique`(`tag_id`, `taggee_id`, `type`) USING BTREE,
                                 INDEX `taggee_type`(`taggee_id`, `type`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for team
-- ----------------------------
DROP TABLE IF EXISTS `team`;
CREATE TABLE `team`  (
                         `value` int(11) NOT NULL AUTO_INCREMENT,
                         `name` varchar(50) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                         PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 22 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for technology
-- ----------------------------
DROP TABLE IF EXISTS `technology`;
CREATE TABLE `technology`  (
                               `value` int(11) NOT NULL AUTO_INCREMENT,
                               `name` varchar(50) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                               PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 33 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for test_results
-- ----------------------------
DROP TABLE IF EXISTS `test_results`;
CREATE TABLE `test_results`  (
                                 `value` int(11) NOT NULL AUTO_INCREMENT,
                                 `name` varchar(20) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                 `background_class` varchar(100) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                 PRIMARY KEY (`value`) USING BTREE,
                                 UNIQUE INDEX `name_unique`(`name`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 4 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for test_status
-- ----------------------------
DROP TABLE IF EXISTS `test_status`;
CREATE TABLE `test_status`  (
                                `value` int(11) NOT NULL AUTO_INCREMENT,
                                `name` varchar(100) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 6 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for threat_catalog
-- ----------------------------
DROP TABLE IF EXISTS `threat_catalog`;
CREATE TABLE `threat_catalog`  (
                                   `id` int(11) NOT NULL AUTO_INCREMENT,
                                   `number` varchar(20) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                   `grouping` int(11) NOT NULL,
                                   `name` varchar(1000) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                   `description` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                   `order` int(11) NOT NULL,
                                   PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 25 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for threat_grouping
-- ----------------------------
DROP TABLE IF EXISTS `threat_grouping`;
CREATE TABLE `threat_grouping`  (
                                    `value` int(11) NOT NULL AUTO_INCREMENT,
                                    `name` varchar(50) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                    `default` tinyint(1) NOT NULL DEFAULT 0,
                                    `order` int(11) NOT NULL,
                                    PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 5 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for user
-- ----------------------------
DROP TABLE IF EXISTS `user`;
CREATE TABLE `user`  (
                         `value` int(11) NOT NULL AUTO_INCREMENT,
                         `enabled` tinyint(1) NOT NULL DEFAULT 1,
                         `lockout` tinyint(4) NOT NULL DEFAULT 0,
                         `type` varchar(20) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'simplerisk',
                         `username` blob NOT NULL,
                         `name` varchar(50) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                         `email` blob NOT NULL,
                         `salt` varchar(20) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                         `password` binary(60) NOT NULL,
                         `last_login` datetime NULL DEFAULT NULL,
                         `last_password_change_date` timestamp NOT NULL DEFAULT current_timestamp(),
                         `role_id` int(11) NOT NULL,
                         `lang` varchar(5) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                         `admin` tinyint(1) NOT NULL DEFAULT 0,
                         `multi_factor` int(11) NOT NULL DEFAULT 1,
                         `change_password` tinyint(4) NOT NULL DEFAULT 0,
                         `custom_display_settings` varchar(1000) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT '',
                         `manager` int(11) NULL DEFAULT NULL,
                         `custom_plan_mitigation_display_settings` varchar(2000) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT '{\"risk_colums\":[[\"id\",\"1\"],[\"risk_status\",\"1\"],[\"subject\",\"1\"],[\"calculated_risk\",\"1\"],[\"submission_date\",\"1\"]],\"mitigation_colums\":[[\"mitigation_planned\",\"1\"]],\"review_colums\":[[\"management_review\",\"1\"]]}\n',
                         `custom_perform_reviews_display_settings` varchar(2000) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT '{\"risk_colums\":[[\"id\",\"1\"],[\"risk_status\",\"1\"],[\"subject\",\"1\"],[\"calculated_risk\",\"1\"],[\"submission_date\",\"1\"]],\"mitigation_colums\":[[\"mitigation_planned\",\"1\"]],\"review_colums\":[[\"management_review\",\"1\"]]}\n',
                         `custom_reviewregularly_display_settings` varchar(2000) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT '{\"risk_colums\":[[\"id\",\"1\"],[\"risk_status\",\"1\"],[\"subject\",\"1\"],[\"calculated_risk\",\"1\"],[\"days_open\",\"1\"]],\"review_colums\":[[\"management_review\",\"0\"],[\"review_date\",\"0\"],[\"next_step\",\"0\"],[\"next_review_date\",\"1\"],[\"comments\",\"0\"]]}',
                         `custom_risks_and_issues_settings` varchar(2000) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
                         PRIMARY KEY (`value`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 50 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for user_pass_history
-- ----------------------------
DROP TABLE IF EXISTS `user_pass_history`;
CREATE TABLE `user_pass_history`  (
                                      `id` int(11) NOT NULL AUTO_INCREMENT,
                                      `user_id` int(11) NOT NULL,
                                      `salt` varchar(20) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
                                      `password` binary(60) NOT NULL,
                                      `add_date` timestamp NOT NULL DEFAULT current_timestamp(),
                                      PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for user_pass_reuse_history
-- ----------------------------
DROP TABLE IF EXISTS `user_pass_reuse_history`;
CREATE TABLE `user_pass_reuse_history`  (
                                            `id` int(11) NOT NULL AUTO_INCREMENT,
                                            `user_id` int(11) NOT NULL,
                                            `password` binary(60) NOT NULL,
                                            `counts` int(11) NOT NULL DEFAULT 1,
                                            PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for user_to_team
-- ----------------------------
DROP TABLE IF EXISTS `user_to_team`;
CREATE TABLE `user_to_team`  (
                                 `user_id` int(11) NOT NULL,
                                 `team_id` int(11) NOT NULL,
                                 PRIMARY KEY (`user_id`, `team_id`) USING BTREE,
                                 INDEX `team_id`(`team_id`, `user_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

SET FOREIGN_KEY_CHECKS = 1;
