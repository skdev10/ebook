-- Migration: Add ProfilePicturePath column to Users table
-- Run this SQL script on your MySQL database

ALTER TABLE `Users` 
ADD COLUMN `ProfilePicturePath` longtext CHARACTER SET utf8mb4 NULL 
AFTER `Status`;

-- Verify the column was added
-- SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE 
-- FROM INFORMATION_SCHEMA.COLUMNS 
-- WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'ProfilePicturePath';

