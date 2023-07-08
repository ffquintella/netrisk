CREATE TABLE `simplerisk`.`addons_client_registration`  (
                                          `Id` int NOT NULL,
                                          `Name` varchar(255) NULL,
                                          `ExternalId` varchar(255) NOT NULL,
                                          `Hostname` varchar(255) NULL,
                                          `LoggedAccount` varchar(255) NULL,
                                          `RegistrationDate` datetime NOT NULL ON UPDATE CURRENT_TIMESTAMP,
                                          `LastVerificationDate` datetime NOT NULL ON UPDATE CURRENT_TIMESTAMP,
                                          `Status` varchar(255) NOT NULL DEFAULT "requested",
                                          PRIMARY KEY (`Id`)
);