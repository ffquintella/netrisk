CREATE TABLE `mitigation_cost`  (
                                          `value` integer NOT NULL AUTO_INCREMENT,
                                          `name` varchar(255) NOT NULL,
                                          PRIMARY KEY (`value`)
);

Insert into `mitigation_cost` VALUES(1, 'Trivial');
Insert into `mitigation_cost` VALUES(2, 'Menor');
Insert into `mitigation_cost` VALUES(3, 'Considerável');
Insert into `mitigation_cost` VALUES(4, 'Significativo');
Insert into `mitigation_cost` VALUES(5, 'Excepcional');