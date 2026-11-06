/* Run against AgriStreamDB using your SQL Server Authentication administrator account. */
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.Users(
  Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
  Username NVARCHAR(50) NOT NULL CONSTRAINT UQ_Users_Username UNIQUE,
  PasswordHash NVARCHAR(256) NOT NULL,
  Role NVARCHAR(20) NOT NULL CONSTRAINT CK_Users_Role CHECK(Role IN ('Farmer','Agronomist','Admin')),
  FullName NVARCHAR(100) NOT NULL,
  CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT SYSUTCDATETIME(),
  IsActive BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1
 );
END;
IF OBJECT_ID(N'dbo.DeviceStatus', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DeviceStatus(
  Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DeviceStatus PRIMARY KEY,
  DeviceId NVARCHAR(50) NOT NULL CONSTRAINT UQ_DeviceStatus_DeviceId UNIQUE,
  FieldId NVARCHAR(50) NOT NULL,
  Status NVARCHAR(30) NOT NULL CONSTRAINT DF_DeviceStatus_Status DEFAULT 'Idle',
  LastUpdated DATETIME2 NOT NULL CONSTRAINT DF_DeviceStatus_LastUpdated DEFAULT SYSUTCDATETIME(),
  LastCommandId NVARCHAR(50) NULL
 );
END;
IF NOT EXISTS(SELECT 1 FROM dbo.DeviceStatus WHERE DeviceId='IRR-001') INSERT dbo.DeviceStatus(DeviceId,FieldId,Status) VALUES('IRR-001','Field-A','Idle');
IF NOT EXISTS(SELECT 1 FROM dbo.DeviceStatus WHERE DeviceId='IRR-002') INSERT dbo.DeviceStatus(DeviceId,FieldId,Status) VALUES('IRR-002','Field-B','Idle');
