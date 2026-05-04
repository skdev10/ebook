-- Run once on MySQL / MariaDB for integrated admin book lifecycle tracking.
-- Creates immutable rows for every book creation and status change (see BookLifecycleSaveChangesInterceptor).

CREATE TABLE IF NOT EXISTS `bookstatetransitions` (
  `BookStateTransitionId` int NOT NULL AUTO_INCREMENT,
  `BookId` int NOT NULL,
  `UserId` int NOT NULL,
  `ActorUserId` int NULL,
  `FromStatus` varchar(100) CHARACTER SET utf8mb4 NULL,
  `ToStatus` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
  `Kind` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
  `MetadataJson` varchar(2000) CHARACTER SET utf8mb4 NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (`BookStateTransitionId`),
  KEY `IX_bookstatetransitions_BookId` (`BookId`),
  KEY `IX_bookstatetransitions_UserId` (`UserId`),
  KEY `IX_bookstatetransitions_CreatedAt` (`CreatedAt`),
  CONSTRAINT `FK_bookstatetransitions_books` FOREIGN KEY (`BookId`) REFERENCES `books` (`BookId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
