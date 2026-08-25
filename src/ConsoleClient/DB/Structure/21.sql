ALTER TABLE `hosts`
    ADD COLUMN IF NOT EXISTS `Properties` text NULL AFTER `MacAddress`;

ALTER TABLE `vulnerabilities`
    ADD COLUMN IF NOT EXISTS `Cvss3BaseScore` float NULL DEFAULT 0 AFTER `HostServiceId`,
    ADD COLUMN IF NOT EXISTS `Cvss3TemporalScore` float NULL DEFAULT 0 AFTER `Cvss3BaseScore`,
    ADD COLUMN IF NOT EXISTS `Cvss3Vector` varchar(255) NULL AFTER `Cvss3TemporalScore`,
    ADD COLUMN IF NOT EXISTS `Cvss3TemporalVector` varchar(255) NULL AFTER `Cvss3Vector`,
    ADD COLUMN IF NOT EXISTS `Cvss3ImpactScore` float NULL DEFAULT 0 AFTER `Cvss3TemporalVector`,
    ADD COLUMN IF NOT EXISTS `CvssBaseScore` float NULL DEFAULT 0 AFTER `Cvss3ImpactScore`,
    ADD COLUMN IF NOT EXISTS `CvssScoreSource` varchar(255) NULL AFTER `CvssBaseScore`,
    ADD COLUMN IF NOT EXISTS `CvssTemporalScore` float NULL DEFAULT 0 AFTER `CvssScoreSource`,
    ADD COLUMN IF NOT EXISTS `CvssTemporalVector` varchar(255) NULL AFTER `CvssTemporalScore`,
    ADD COLUMN IF NOT EXISTS `CvssVector` varchar(255) NULL AFTER `CvssTemporalVector`,
    ADD COLUMN IF NOT EXISTS `ExploitAvaliable` bool AFTER `CvssVector`,
    ADD COLUMN IF NOT EXISTS `ExploitCodeMaturity` varchar(255) NULL AFTER `ExploitAvaliable`,
    ADD COLUMN IF NOT EXISTS `ExploitabilityEasy` varchar(255) NULL AFTER `ExploitCodeMaturity`,
    ADD COLUMN IF NOT EXISTS `ExploitedByScanner` bool AFTER `ExploitabilityEasy`,
    ADD COLUMN IF NOT EXISTS `PatchPublicationDate` datetime NULL AFTER `ExploitedByScanner`,
    ADD COLUMN IF NOT EXISTS `ThreatIntensity` varchar(255) NULL AFTER `PatchPublicationDate`,
    ADD COLUMN IF NOT EXISTS `ThreatRecency` varchar(255) NULL AFTER `ThreatIntensity`,
    ADD COLUMN IF NOT EXISTS `ThreatSources` varchar(255) NULL AFTER `ThreatRecency`,
    ADD COLUMN IF NOT EXISTS `Cves` text NULL AFTER `ThreatSources`,
    ADD COLUMN IF NOT EXISTS `VprScore` float NULL AFTER `Cves`,
    ADD COLUMN IF NOT EXISTS `VulnerabilityPublicationDate` datetime NULL AFTER `VprScore`,
    ADD COLUMN IF NOT EXISTS `Xref` text NULL AFTER `VulnerabilityPublicationDate`,
    ADD COLUMN IF NOT EXISTS `Iava` text NULL AFTER `Xref`,
    ADD COLUMN IF NOT EXISTS `Msft` text NULL AFTER `Iava`,
    ADD COLUMN IF NOT EXISTS `Mskb` text NULL AFTER `Msft`;