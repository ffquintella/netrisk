CREATE TABLE `simplerisk`.`mitigation_cost`  (
                                          `value` integer NOT NULL AUTO_INCREMENT,
                                          `name` varchar(255) NOT NULL,
                                          PRIMARY KEY (`value`)
);

Insert into `simplerisk`.`mitigation_cost` VALUES(1, 'Trivial');
Insert into `simplerisk`.`mitigation_cost` VALUES(2, 'Menor');
Insert into `simplerisk`.`mitigation_cost` VALUES(3, 'Considerável');
Insert into `simplerisk`.`mitigation_cost` VALUES(4, 'Significativo');
Insert into `simplerisk`.`mitigation_cost` VALUES(5, 'Excepcional');