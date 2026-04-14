-- Migration: Add Admin Dashboard Tables
-- Run this SQL script to create the required tables for the Admin Dashboard

-- Create Notifications table
CREATE TABLE IF NOT EXISTS `Notifications` (
    `NotificationId` int NOT NULL AUTO_INCREMENT,
    `UserId` int NULL,
    `Title` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `Message` varchar(1000) CHARACTER SET utf8mb4 NOT NULL,
    `Type` varchar(50) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'Info',
    `IsRead` tinyint(1) NOT NULL DEFAULT 0,
    `Link` varchar(500) CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `ReadAt` datetime(6) NULL,
    CONSTRAINT `PK_Notifications` PRIMARY KEY (`NotificationId`),
    CONSTRAINT `FK_Notifications_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`UserId`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4;

-- Create Settings table
CREATE TABLE IF NOT EXISTS `Settings` (
    `SettingId` int NOT NULL AUTO_INCREMENT,
    `Key` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `Value` varchar(1000) CHARACTER SET utf8mb4 NULL,
    `Category` varchar(50) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'General',
    `Description` varchar(200) CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_Settings` PRIMARY KEY (`SettingId`),
    UNIQUE KEY `IX_Settings_Key` (`Key`)
) CHARACTER SET=utf8mb4;

-- Create AuditLogs table
CREATE TABLE IF NOT EXISTS `AuditLogs` (
    `AuditLogId` int NOT NULL AUTO_INCREMENT,
    `UserId` int NULL,
    `Action` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `EntityType` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `EntityId` int NULL,
    `Description` varchar(500) CHARACTER SET utf8mb4 NULL,
    `IpAddress` varchar(50) CHARACTER SET utf8mb4 NULL,
    `UserAgent` varchar(500) CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_AuditLogs` PRIMARY KEY (`AuditLogId`),
    CONSTRAINT `FK_AuditLogs_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`UserId`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4;

-- Create UserPreferences table
CREATE TABLE IF NOT EXISTS `UserPreferences` (
    `PreferenceId` int NOT NULL AUTO_INCREMENT,
    `UserId` int NOT NULL,
    `Key` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `Value` varchar(500) CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_UserPreferences` PRIMARY KEY (`PreferenceId`),
    CONSTRAINT `FK_UserPreferences_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`UserId`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

-- Create indexes for better performance
CREATE INDEX IF NOT EXISTS `IX_Notifications_UserId` ON `Notifications` (`UserId`);
CREATE INDEX IF NOT EXISTS `IX_Notifications_IsRead` ON `Notifications` (`IsRead`);
CREATE INDEX IF NOT EXISTS `IX_Notifications_CreatedAt` ON `Notifications` (`CreatedAt`);
CREATE INDEX IF NOT EXISTS `IX_Settings_Category` ON `Settings` (`Category`);
CREATE INDEX IF NOT EXISTS `IX_AuditLogs_UserId` ON `AuditLogs` (`UserId`);
CREATE INDEX IF NOT EXISTS `IX_AuditLogs_EntityType` ON `AuditLogs` (`EntityType`);
CREATE INDEX IF NOT EXISTS `IX_AuditLogs_CreatedAt` ON `AuditLogs` (`CreatedAt`);
CREATE INDEX IF NOT EXISTS `IX_UserPreferences_UserId_Key` ON `UserPreferences` (`UserId`, `Key`);

-- Insert sample settings (optional)
INSERT INTO `Settings` (`Key`, `Value`, `Category`, `Description`, `CreatedAt`, `UpdatedAt`) VALUES
('site_name', 'eBook Dashboard', 'General', 'Site name', NOW(), NOW()),
('site_email', 'admin@example.com', 'General', 'Site email', NOW(), NOW()),
('currency', 'USD', 'Payment', 'Default currency', NOW(), NOW()),
('smtp_host', 'smtp.example.com', 'Email', 'SMTP host', NOW(), NOW()),
('smtp_port', '587', 'Email', 'SMTP port', NOW(), NOW()),
('max_login_attempts', '5', 'Security', 'Maximum login attempts', NOW(), NOW()),
('session_timeout', '30', 'Security', 'Session timeout in minutes', NOW(), NOW())
ON DUPLICATE KEY UPDATE `UpdatedAt` = NOW();

-- Insert sample notification (optional)
INSERT INTO `Notifications` (`UserId`, `Title`, `Message`, `Type`, `IsRead`, `CreatedAt`) VALUES
(NULL, 'Welcome to Admin Dashboard', 'Your admin dashboard has been set up successfully!', 'Success', 0, NOW()),
(NULL, 'System Update', 'A new update is available. Please review the changelog.', 'Info', 0, NOW())
ON DUPLICATE KEY UPDATE `CreatedAt` = NOW();

