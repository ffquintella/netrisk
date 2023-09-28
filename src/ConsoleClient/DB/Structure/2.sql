ALTER TABLE `netrisk`.`user_to_team`
    ADD CONSTRAINT `fk_ut_team` FOREIGN KEY (`team_id`) REFERENCES `netrisk`.`team` (`value`) ON DELETE CASCADE,
    ADD CONSTRAINT `fk_ut_user` FOREIGN KEY (`user_id`) REFERENCES `netrisk`.`user` (`value`) ON DELETE CASCADE;