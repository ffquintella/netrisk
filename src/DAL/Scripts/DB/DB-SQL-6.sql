CREATE TABLE `links`  (
          `id` int NOT NULL,
          `key_hash` varchar(255) NOT NULL,
          `type` varchar(255) NOT NULL,
          `creation_date` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
          `expiration_date` datetime NULL,
          `data` blob NULL,
          PRIMARY KEY (`id`),
          UNIQUE INDEX `key_type_idx`(`key_hash`, `type`) USING BTREE,
          INDEX `expiration_date_idx`(`expiration_date`) USING BTREE
);