-- Whole-book AI HTML fragment for formatter preview and PDF export.
-- Run on MySQL when upgrading an existing database (ebookpublications).

ALTER TABLE `books`
  ADD COLUMN `BookContentHtml` LONGTEXT NULL;
