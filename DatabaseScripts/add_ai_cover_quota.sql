-- Lifetime AI cover quota. deploy/ensure-users-schema.sh skips when columns exist.
-- Table name matches EF: [Table("users")]

ALTER TABLE `users`
    ADD COLUMN `AICoverGenerationsUsed` INT NOT NULL DEFAULT 0;

ALTER TABLE `users`
    ADD COLUMN `AICoverGenerationLimit` INT NOT NULL DEFAULT 5;
