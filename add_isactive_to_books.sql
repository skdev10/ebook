-- Add isActive column to books table
-- Run this SQL script on your MySQL database
-- isActive=1 means "Currently Working On" in Dashboard
-- Skip if column already exists

ALTER TABLE `books` 
ADD COLUMN `isActive` int NOT NULL DEFAULT 0;
