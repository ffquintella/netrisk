﻿ALTER TABLE `entities`
    ADD COLUMN `Parent` int NULL AFTER `Status`,
ADD CONSTRAINT `fk_parent` FOREIGN KEY (`Parent`) REFERENCES `entities` (`Id`) ON DELETE CASCADE ON UPDATE CASCADE;