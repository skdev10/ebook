-- Create bookcoverpages table for Book Cover Design Template dropdown
-- Run this script in your MySQL database if the table does not exist.

CREATE TABLE IF NOT EXISTS bookcoverpages (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    Description VARCHAR(500) NULL
);

-- Seed sample templates (run once; skip the INSERT if you already have data)
INSERT INTO bookcoverpages (Description) VALUES
('Simple text front only'),
('Full wrap with spine text'),
('Minimal with author line'),
('Centered title and subtitle'),
('Image full bleed with overlay text');
