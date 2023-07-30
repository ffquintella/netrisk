UPDATE files, file_types
SET files.type = CAST(file_types.`value` AS CHAR)
where files.type = file_types.`name` ;