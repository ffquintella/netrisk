ALTER TABLE `user_to_team`
    ADD CONSTRAINT `fk_ut_team` FOREIGN KEY (`team_id`) REFERENCES `team` (`value`) ON DELETE CASCADE,
    ADD CONSTRAINT `fk_ut_user` FOREIGN KEY (`user_id`) REFERENCES `user` (`value`) ON DELETE CASCADE;