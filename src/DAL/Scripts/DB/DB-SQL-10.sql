CREATE TABLE `risk_to_entity`  (
                                          `risk_id` int NOT NULL,
                                          `entity_id` int NOT NULL,
                                          PRIMARY KEY (`risk_id`, `entity_id`),
                                          CONSTRAINT `fk_risk_id` FOREIGN KEY (`risk_id`) REFERENCES `risks` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION,
                                          CONSTRAINT `fk_entity_id` FOREIGN KEY (`entity_id`) REFERENCES `entities` (`Id`) ON DELETE CASCADE ON UPDATE NO ACTION
);