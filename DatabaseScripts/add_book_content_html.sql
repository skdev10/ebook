-- Whole-book AI HTML fragment for formatter preview and PDF export.
-- Idempotency: deploy/ensure-books-schema.sh skips when column already exists.

ALTER TABLE `books`
  ADD COLUMN `BookContentHtml` LONGTEXT NULL;
