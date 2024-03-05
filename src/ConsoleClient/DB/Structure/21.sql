ALTER TABLE `hosts`
    ADD COLUMN `Properties` text NULL AFTER `MacAddress`;

ALTER TABLE `vulnerabilities`
    ADD COLUMN `Cvss3BaseScore` float NULL DEFAULT 0 AFTER `HostServiceId`,
    ADD COLUMN `Cvss3TemporalScore` float NULL DEFAULT 0 AFTER `Cvss3BaseScore`,
    ADD COLUMN `Cvss3Vector` varchar(255) NULL AFTER `Cvss3TemporalScore`,
    ADD COLUMN `Cvss3TemporalVector` varchar(255) NULL AFTER `Cvss3Vector`,
    ADD COLUMN `Cvss3ImpactScore` float NULL DEFAULT 0 AFTER `Cvss3TemporalVector`,
    ADD COLUMN `CvssBaseScore` float NULL DEFAULT 0 AFTER `Cvss3ImpactScore`,
    ADD COLUMN `CvssScoreSource` varchar(255) NULL AFTER `CvssBaseScore`,
    ADD COLUMN `CvssTemporalScore` float NULL DEFAULT 0 AFTER `CvssScoreSource`,
    ADD COLUMN `CvssTemporalVector` varchar(255) NULL AFTER `CvssTemporalScore`,
    ADD COLUMN `CvssVector` varchar(255) NULL AFTER `CvssTemporalVector`,
    ADD COLUMN `ExploitAvaliable` bool AFTER `CvssVector`,
    ADD COLUMN `ExploitCodeMaturity` varchar(255) NULL AFTER `ExploitAvaliable`,
    ADD COLUMN `ExploitabilityEasy` varchar(255) NULL AFTER `ExploitCodeMaturity`,
    ADD COLUMN `ExploitedByScanner` bool AFTER `ExploitabilityEasy`,
    ADD COLUMN `PatchPublicationDate` datetime NULL AFTER `ExploitedByScanner`,
    ADD COLUMN `ThreatIntensity` varchar(255) NULL AFTER `PatchPublicationDate`,
    ADD COLUMN `ThreatRecency` varchar(255) NULL AFTER `ThreatIntensity`,
    ADD COLUMN `ThreatSources` varchar(255) NULL AFTER `ThreatRecency`,
    ADD COLUMN `Cves` text NULL AFTER `ThreatSources`,
    ADD COLUMN `VprScore` float NULL AFTER `Cves`,
    ADD COLUMN `VulnerabilityPublicationDate` datetime NULL AFTER `VprScore`,
    ADD COLUMN `Xref` text NULL AFTER `VulnerabilityPublicationDate`,
    ADD COLUMN `Iava` text NULL AFTER `Xref`,
    ADD COLUMN `Msft` text NULL AFTER `Iava`,
    ADD COLUMN `Mskb` text NULL AFTER `Msft`;