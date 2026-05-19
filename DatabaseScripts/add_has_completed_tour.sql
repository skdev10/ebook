-- One-time onboarding migration:
-- 1) Existing users are marked completed so they do not see auto-tour.
-- 2) New users default to 0 (false) and will see tour once.

ALTER TABLE `Users`
    ADD COLUMN IF NOT EXISTS `HasCompletedTour` TINYINT(1) NULL;

UPDATE `Users`
SET `HasCompletedTour` = 1
WHERE `HasCompletedTour` IS NULL;

ALTER TABLE `Users`
    MODIFY COLUMN `HasCompletedTour` TINYINT(1) NOT NULL DEFAULT 0;
