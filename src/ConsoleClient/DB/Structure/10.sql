ALTER TABLE `risk_scoring`
DROP COLUMN `CVSS_AccessVector`,
DROP COLUMN `CVSS_AccessComplexity`,
DROP COLUMN `CVSS_Authentication`,
DROP COLUMN `CVSS_ConfImpact`,
DROP COLUMN `CVSS_IntegImpact`,
DROP COLUMN `CVSS_AvailImpact`,
DROP COLUMN `CVSS_Exploitability`,
DROP COLUMN `CVSS_RemediationLevel`,
DROP COLUMN `CVSS_ReportConfidence`,
DROP COLUMN `CVSS_CollateralDamagePotential`,
DROP COLUMN `CVSS_TargetDistribution`,
DROP COLUMN `CVSS_ConfidentialityRequirement`,
DROP COLUMN `CVSS_IntegrityRequirement`,
DROP COLUMN `CVSS_AvailabilityRequirement`,
DROP COLUMN `DREAD_DamagePotential`,
DROP COLUMN `DREAD_Reproducibility`,
DROP COLUMN `DREAD_Exploitability`,
DROP COLUMN `DREAD_AffectedUsers`,
DROP COLUMN `DREAD_Discoverability`,
DROP COLUMN `OWASP_SkillLevel`,
DROP COLUMN `OWASP_Motive`,
DROP COLUMN `OWASP_Opportunity`,
DROP COLUMN `OWASP_Size`,
DROP COLUMN `OWASP_EaseOfDiscovery`,
DROP COLUMN `OWASP_EaseOfExploit`,
DROP COLUMN `OWASP_Awareness`,
DROP COLUMN `OWASP_IntrusionDetection`,
DROP COLUMN `OWASP_LossOfConfidentiality`,
DROP COLUMN `OWASP_LossOfIntegrity`,
DROP COLUMN `OWASP_LossOfAvailability`,
DROP COLUMN `OWASP_LossOfAccountability`,
DROP COLUMN `OWASP_FinancialDamage`,
DROP COLUMN `OWASP_ReputationDamage`,
DROP COLUMN `OWASP_NonCompliance`,
DROP COLUMN `OWASP_PrivacyViolation`,
DROP COLUMN `Contributing_Likelihood`,
ADD COLUMN `contributing_score` double NULL AFTER `Custom`;