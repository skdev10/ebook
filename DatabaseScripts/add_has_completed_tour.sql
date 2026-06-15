-- One-time onboarding migration (table name matches EF: [Table("users")]):
-- 1) Existing users are marked completed so they do not see auto-tour.
-- 2) New users default to 0 (false) and will see tour once.

ALTER TABLE `users`
    ADD COLUMN IF NOT EXISTS `HasCompletedTour` TINYINT(1) NULL;

UPDATE `users`
SET `HasCompletedTour` = 1
WHERE `HasCompletedTour` IS NULL;

ALTER TABLE `users`
    MODIFY COLUMN `HasCompletedTour` TINYINT(1) NOT NULL DEFAULT 0;
