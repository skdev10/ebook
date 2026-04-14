-- Add Reading Statistics columns to users table (MySQL)
-- Run this script once. If a column already exists, skip that line or comment it out.
-- Books Read is derived from Books (count Published); these store Hours Read, Pages Read, Day Streak.

ALTER TABLE `users` ADD COLUMN `ReadingHours` INT NULL;
ALTER TABLE `users` ADD COLUMN `PagesRead` INT NULL;
ALTER TABLE `users` ADD COLUMN `ReadingStreak` INT NULL;
