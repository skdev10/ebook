-- Migration: Create ParsedBookContent table
-- Run this SQL script to create the ParsedBookContent table for storing parsed book content
-- This table stores the breakdown of ResponseData from apirawresponse table

CREATE TABLE IF NOT EXISTS `parsedbookcontent` (
    `ParsedContentId` int NOT NULL AUTO_INCREMENT,
    `ResponseId` int NOT NULL,
    `BookId` int NULL,
    `UserId` int NULL,
    `ChapterTitle` longtext CHARACTER SET utf8mb4 NULL,
    `BookSectionsJson` longtext CHARACTER SET utf8mb4 NULL,
    `HighlightChapterName` longtext CHARACTER SET utf8mb4 NULL,
    `HighlightsJson` longtext CHARACTER SET utf8mb4 NULL,
    `FullParsedDataJson` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_parsedbookcontent` PRIMARY KEY (`ParsedContentId`),
    CONSTRAINT `FK_parsedbookcontent_apirawresponse_ResponseId` FOREIGN KEY (`ResponseId`) REFERENCES `apirawresponse` (`ResponseId`) ON DELETE CASCADE,
    INDEX `IX_parsedbookcontent_ResponseId` (`ResponseId`),
    INDEX `IX_parsedbookcontent_UserId_BookId` (`UserId`, `BookId`),
    INDEX `IX_parsedbookcontent_BookId` (`BookId`),
    INDEX `IX_parsedbookcontent_UserId` (`UserId`)
) CHARACTER SET=utf8mb4;

-- Verify the table was created
-- SELECT TABLE_NAME, TABLE_SCHEMA 
-- FROM INFORMATION_SCHEMA.TABLES 
-- WHERE TABLE_NAME = 'parsedbookcontent';
