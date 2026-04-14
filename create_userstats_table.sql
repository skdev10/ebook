-- Create UserStats table for per-user reading statistics (Hours Read, Pages Read, Day Streak).
-- Books Published and Books Generated are derived from the Books table; this table stores editable stats.
-- Run this script once on your MySQL database.

CREATE TABLE IF NOT EXISTS `userstats` (
  `UserId` int NOT NULL,
  `HoursRead` int NOT NULL DEFAULT 0,
  `PagesRead` int NOT NULL DEFAULT 0,
  `DayStreak` int NOT NULL DEFAULT 0,
  `UpdatedAt` datetime(6) NULL,
  PRIMARY KEY (`UserId`),
  CONSTRAINT `FK_UserStats_Users` FOREIGN KEY (`UserId`) REFERENCES `users` (`UserId`) ON DELETE CASCADE
);
