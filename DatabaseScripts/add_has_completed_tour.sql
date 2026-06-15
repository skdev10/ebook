-- One-time onboarding migration (table name matches EF: [Table("users")]):
-- 1) Existing users are marked completed so they do not see auto-tour.
-- 2) New users default to 0 (false) and will see tour once.
-- Idempotency: deploy/ensure-users-schema.sh skips when column already exists.

ALTER TABLE `users`
    ADD COLUMN `HasCompletedTour` TINYINT(1) NULL;

UPDATE `users`
SET `HasCompletedTour` = 1
WHERE `HasCompletedTour` IS NULL;

ALTER TABLE `users`
    MODIFY COLUMN `HasCompletedTour` TINYINT(1) NOT NULL DEFAULT 0;
