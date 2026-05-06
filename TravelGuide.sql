CREATE DATABASE TravelGuideDb;
GO

USE TravelGuideDb;
GO

-- Tài khoản web quản trị: admin + chủ quán (owner)
CREATE TABLE dbo.UserAccount
(
    Id                    INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Username              NVARCHAR(120) NOT NULL UNIQUE,
    PasswordHash          NVARCHAR(256) NOT NULL,
    DisplayName           NVARCHAR(200) NOT NULL,
    Role                  NVARCHAR(30) NOT NULL CHECK (Role IN (N'admin', N'owner')),
    IsLocked              BIT NOT NULL DEFAULT 0,
    RegistrationApproved  BIT NOT NULL DEFAULT 1,
    CreatedAtUtc          DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc          DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

-- Tài khoản du khách (app)
CREATE TABLE dbo.TouristUser
(
    Id            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Username      NVARCHAR(100) NOT NULL UNIQUE,
    PasswordHash  NVARCHAR(256) NOT NULL,
    DisplayName   NVARCHAR(200) NOT NULL,
    AccountTier   NVARCHAR(20) NOT NULL DEFAULT N'free' CHECK (AccountTier IN (N'free', N'premium')),
    CreatedAtUtc  DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc  DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

-- Bảng địa điểm (POI)
CREATE TABLE dbo.Poi
(
    Id            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    NameVi        NVARCHAR(300) NOT NULL,
    NameEn        NVARCHAR(300) NOT NULL DEFAULT N'',
    NameJa        NVARCHAR(300) NOT NULL DEFAULT N'',
    NameKo        NVARCHAR(300) NOT NULL DEFAULT N'',
    NameZh        NVARCHAR(300) NOT NULL DEFAULT N'',
    DescVi        NVARCHAR(MAX) NOT NULL,
    DescEn        NVARCHAR(MAX) NOT NULL DEFAULT N'',
    DescJa        NVARCHAR(MAX) NOT NULL DEFAULT N'',
    DescKo        NVARCHAR(MAX) NOT NULL DEFAULT N'',
    DescZh        NVARCHAR(MAX) NOT NULL DEFAULT N'',
    Latitude      FLOAT NOT NULL,
    Longitude     FLOAT NOT NULL,
    Radius        FLOAT NOT NULL DEFAULT 50,
    ImagePath     NVARCHAR(500) NOT NULL DEFAULT N'',
    AudioUrl      NVARCHAR(1000) NOT NULL DEFAULT N'',
    Status        NVARCHAR(30) NOT NULL DEFAULT N'published' CHECK (Status IN (N'published', N'pending', N'rejected')),
    RejectReason  NVARCHAR(1000) NOT NULL DEFAULT N'',
    OwnerUserId   INT NOT NULL DEFAULT 0,
    Priority      INT NOT NULL DEFAULT 0,
    MapLink       NVARCHAR(1000) NOT NULL DEFAULT N'',
    Price         DECIMAL(18,2) NOT NULL DEFAULT 0,
    Tag           NVARCHAR(100) NOT NULL DEFAULT N'Quán ăn',
    CreatedAtUtc  DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc  DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE INDEX IX_Poi_Status_Priority_Id ON dbo.Poi(Status, Priority DESC, Id ASC);
GO

CREATE INDEX IX_Poi_OwnerUserId ON dbo.Poi(OwnerUserId, Id ASC);
GO

-- Phiên đăng nhập du khách theo thiết bị
CREATE TABLE dbo.RefreshToken
(
    Id             BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TouristUserId  INT NOT NULL,
    TokenHash      NVARCHAR(256) NOT NULL,
    DeviceId       NVARCHAR(120) NULL,
    UserAgent      NVARCHAR(500) NULL,
    IpAddress      NVARCHAR(64) NULL,
    ExpiresAtUtc   DATETIME2(0) NOT NULL,
    RevokedAtUtc   DATETIME2(0) NULL,
    CreatedAtUtc   DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    FOREIGN KEY (TouristUserId) REFERENCES dbo.TouristUser(Id)
);
GO

CREATE UNIQUE INDEX UX_RefreshToken_TokenHash ON dbo.RefreshToken(TokenHash);
GO

CREATE INDEX IX_RefreshToken_TouristUserId ON dbo.RefreshToken(TouristUserId, ExpiresAtUtc DESC);
GO
