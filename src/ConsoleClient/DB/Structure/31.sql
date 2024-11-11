CREATE TABLE `RisksToCatalog` (
                                  `RiskId` int(11) NOT NULL,
                                  `RiskCatalogId` int(11) NOT NULL,
                                  CONSTRAINT `PRIMARY` PRIMARY KEY (`RiskId`, `RiskCatalogId`),
                                  CONSTRAINT `fk_risk_rcatalog` FOREIGN KEY (`RiskCatalogId`) REFERENCES `risk_catalog` (`id`) ON DELETE CASCADE,
                                  CONSTRAINT `fk_riskcatlog_risk_2` FOREIGN KEY (`RiskId`) REFERENCES `risks` (`id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE INDEX `IX_RisksToCatalog_RiskCatalogId` ON `RisksToCatalog` (`RiskCatalogId`);