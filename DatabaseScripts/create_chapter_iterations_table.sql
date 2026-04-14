-- =============================================================================
-- Chapter iterations: each AI generation is a separate row; same logical chapter
-- shares ChapterSeriesGuid. Run once on your MySQL server (e.g. after backup).
-- =============================================================================
CREATE TABLE IF NOT EXISTS chapter_iterations (
  ChapterIterationId INT NOT NULL AUTO_INCREMENT,
  ChapterSeriesGuid CHAR(36) NOT NULL COMMENT 'Links all draft/final versions of one chapter slot (book + chapter number + user)',
  BookId INT NOT NULL,
  UserId INT NOT NULL,
  ChapterNumber INT NOT NULL,
  IterationNumber INT NOT NULL COMMENT '1-based sequence within the series (regenerate increments)',
  ResponseId INT NULL COMMENT 'Optional link to apirawresponse.ResponseId',
  Title VARCHAR(500) NULL,
  Content LONGTEXT NOT NULL,
  GenerationDate DATE NOT NULL COMMENT 'UTC date of generation (separate from time per spec)',
  GenerationTime TIME(6) NOT NULL COMMENT 'UTC time of generation',
  IsFinalized TINYINT(1) NOT NULL DEFAULT 0,
  IsLocked TINYINT(1) NOT NULL DEFAULT 0 COMMENT 'True when this iteration was finalized (no further edits)',
  FinalizedDate DATE NULL COMMENT 'UTC date when marked final',
  FinalizedTime TIME(6) NULL COMMENT 'UTC time when marked final',
  PRIMARY KEY (ChapterIterationId),
  UNIQUE KEY UK_chapter_iterations_series_iter (ChapterSeriesGuid, IterationNumber),
  UNIQUE KEY UK_chapter_iterations_response (ResponseId),
  KEY IX_chapter_iterations_user_book_chapter (UserId, BookId, ChapterNumber),
  KEY IX_chapter_iterations_series (ChapterSeriesGuid),
  KEY IX_chapter_iterations_export (UserId, BookId, IsFinalized, ChapterNumber)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
