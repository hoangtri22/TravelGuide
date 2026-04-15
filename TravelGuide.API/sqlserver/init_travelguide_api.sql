/*
  TravelGuide.Api - SQL Server bootstrap script
  Run in SSMS (SQL Server Management Studio).
*/

-- 1) Create database if missing
IF DB_ID(N'TravelGuideApiDb') IS NULL
BEGIN
    CREATE DATABASE TravelGuideApiDb;
END
GO

USE TravelGuideApiDb;
GO

-- 2) Tourist users (for app login/register)
IF OBJECT_ID(N'dbo.TouristUser', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TouristUser
    (
        Id            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Username      NVARCHAR(100) NOT NULL,
        PasswordHash  NVARCHAR(256) NOT NULL,
        DisplayName   NVARCHAR(200) NOT NULL,
        AccountTier   NVARCHAR(20) NOT NULL CONSTRAINT DF_TouristUser_AccountTier DEFAULT N'free',
        CreatedAtUtc  DATETIME2(0) NOT NULL CONSTRAINT DF_TouristUser_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc  DATETIME2(0) NOT NULL CONSTRAINT DF_TouristUser_UpdatedAtUtc DEFAULT SYSUTCDATETIME()
    );
END
GO

IF COL_LENGTH('dbo.TouristUser', 'AccountTier') IS NULL
BEGIN
    ALTER TABLE dbo.TouristUser
        ADD AccountTier NVARCHAR(20) NOT NULL CONSTRAINT DF_TouristUser_AccountTier_Migrated DEFAULT N'free';
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_TouristUser_AccountTier'
      AND parent_object_id = OBJECT_ID(N'dbo.TouristUser')
)
BEGIN
    ALTER TABLE dbo.TouristUser
        ADD CONSTRAINT CK_TouristUser_AccountTier CHECK (AccountTier IN (N'free', N'premium'));
END
GO

-- Case-insensitive unique username
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_TouristUser_Username'
      AND object_id = OBJECT_ID(N'dbo.TouristUser')
)
BEGIN
    CREATE UNIQUE INDEX UX_TouristUser_Username
        ON dbo.TouristUser(Username);
END
GO

-- 3) Optional public POI table for API (if later moved from JSON to DB)
IF OBJECT_ID(N'dbo.PublicPoi', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PublicPoi
    (
        Id            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        NameVi        NVARCHAR(300) NOT NULL,
        NameEn        NVARCHAR(300) NOT NULL CONSTRAINT DF_PublicPoi_NameEn DEFAULT N'',
        NameJa        NVARCHAR(300) NOT NULL CONSTRAINT DF_PublicPoi_NameJa DEFAULT N'',
        NameKo        NVARCHAR(300) NOT NULL CONSTRAINT DF_PublicPoi_NameKo DEFAULT N'',
        NameZh        NVARCHAR(300) NOT NULL CONSTRAINT DF_PublicPoi_NameZh DEFAULT N'',
        DescVi        NVARCHAR(MAX) NOT NULL,
        DescEn        NVARCHAR(MAX) NOT NULL CONSTRAINT DF_PublicPoi_DescEn DEFAULT N'',
        DescJa        NVARCHAR(MAX) NOT NULL CONSTRAINT DF_PublicPoi_DescJa DEFAULT N'',
        DescKo        NVARCHAR(MAX) NOT NULL CONSTRAINT DF_PublicPoi_DescKo DEFAULT N'',
        DescZh        NVARCHAR(MAX) NOT NULL CONSTRAINT DF_PublicPoi_DescZh DEFAULT N'',
        Latitude      FLOAT NOT NULL,
        Longitude     FLOAT NOT NULL,
        Radius        FLOAT NOT NULL CONSTRAINT DF_PublicPoi_Radius DEFAULT 50,
        ImagePath     NVARCHAR(500) NOT NULL CONSTRAINT DF_PublicPoi_ImagePath DEFAULT N'',
        AudioUrl      NVARCHAR(1000) NOT NULL CONSTRAINT DF_PublicPoi_AudioUrl DEFAULT N'',
        QrImagePath   NVARCHAR(1000) NOT NULL CONSTRAINT DF_PublicPoi_QrImagePath DEFAULT N'',
        Priority      INT NOT NULL CONSTRAINT DF_PublicPoi_Priority DEFAULT 0,
        MapLink       NVARCHAR(1000) NOT NULL CONSTRAINT DF_PublicPoi_MapLink DEFAULT N'',
        Price         DECIMAL(18,2) NOT NULL CONSTRAINT DF_PublicPoi_Price DEFAULT 0,
        Tag           NVARCHAR(100) NOT NULL CONSTRAINT DF_PublicPoi_Tag DEFAULT N'Địa Điểm Du Lịch',
        IsPublished   BIT NOT NULL CONSTRAINT DF_PublicPoi_IsPublished DEFAULT 1,
        CreatedAtUtc  DATETIME2(0) NOT NULL CONSTRAINT DF_PublicPoi_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc  DATETIME2(0) NOT NULL CONSTRAINT DF_PublicPoi_UpdatedAtUtc DEFAULT SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_PublicPoi_IsPublished_Priority'
      AND object_id = OBJECT_ID(N'dbo.PublicPoi')
)
BEGIN
    CREATE INDEX IX_PublicPoi_IsPublished_Priority
        ON dbo.PublicPoi(IsPublished, Priority DESC, Id ASC);
END
GO
