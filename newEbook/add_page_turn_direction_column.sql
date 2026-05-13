-- Add PageTurnDirection column to coverdesigncalculator if missing
-- Run this if you get: "Unknown column 'PageTurnDirection' in 'field list'"

-- MySQL: Add column (ignore error if column already exists)
ALTER TABLE `coverdesigncalculator`
ADD COLUMN `PageTurnDirection` varchar(100) NULL DEFAULT NULL AFTER `PaperType`;
