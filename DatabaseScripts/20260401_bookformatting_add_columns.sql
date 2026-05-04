-- Run on MySQL when upgrading an existing database (adds persistent line spacing + primary platform).
-- Table name matches [Table("bookformatting")] on BookFormatting.
--
-- If you see "Unknown column 'b.LineSpacing' in 'field list'" the app is newer than the DB: run this script.

-- Option A (keeps column order after TextSize). If this errors on AFTER, use Option B below.
ALTER TABLE `bookformatting`
  ADD COLUMN `LineSpacing` VARCHAR(20) NOT NULL DEFAULT '1.6' AFTER `TextSize`,
  ADD COLUMN `PublishingPlatform` VARCHAR(120) NULL AFTER `LineSpacing`;

-- Option B (if Option A fails: wrong column name or order on your server)
-- ALTER TABLE `bookformatting` ADD COLUMN `LineSpacing` VARCHAR(20) NOT NULL DEFAULT '1.6';
-- ALTER TABLE `bookformatting` ADD COLUMN `PublishingPlatform` VARCHAR(120) NULL;

-- Optional: backfill primary platform from comma list
-- UPDATE bookformatting SET PublishingPlatform = TRIM(SUBSTRING_INDEX(PublishingPlatforms, ',', 1))
-- WHERE (PublishingPlatform IS NULL OR PublishingPlatform = '') AND PublishingPlatforms IS NOT NULL AND PublishingPlatforms <> '';
