-- Create bookformatting table for ebookpublications database
-- Run this script if you get: Table 'ebookpublications.bookformatting' doesn't exist

USE ebookpublications;

CREATE TABLE IF NOT EXISTS bookformatting (
  Id int NOT NULL AUTO_INCREMENT,
  BookId int NOT NULL,
  UserId int NOT NULL,
  `Format` varchar(50) NOT NULL DEFAULT 'Ebook',
  InteriorStyle varchar(50) NOT NULL DEFAULT 'Novel',
  TextSize varchar(20) NOT NULL DEFAULT 'Medium',
  PublishingPlatforms varchar(500) DEFAULT NULL,
  CreatedAt datetime(6) NOT NULL,
  UpdatedAt datetime(6) NOT NULL,
  PRIMARY KEY (Id),
  KEY IX_BookFormatting_BookId_UserId (BookId, UserId)
);
