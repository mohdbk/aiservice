/* PDPL: candidates' data requests (a copy of their data, deleting their account), consent records, and how long data is kept. */
CREATE TABLE ops.DataRequest (
  Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DataRequest PRIMARY KEY,
  ApplicantAccountId int NOT NULL CONSTRAINT FK_DataRequest_Account REFERENCES [identity].ApplicantAccount (Id),
  Kind varchar(10) NOT NULL CONSTRAINT CK_DataRequest_Kind CHECK (Kind IN ('Export', 'Deletion')),
  Status varchar(10) NOT NULL CONSTRAINT CK_DataRequest_Status CHECK (Status IN ('Open', 'Completed', 'Declined')),
  Requester nvarchar(400) NOT NULL,
  Reason nvarchar(2000) NOT NULL,
  Response nvarchar(2000) NOT NULL,
  CreatedAt datetime2(3) NOT NULL,
  ClosedAt datetime2(3) NULL,
  ClosedByStaffId int NULL
);
GO
CREATE INDEX IX_DataRequest_Status ON ops.DataRequest (Status, CreatedAt);
GO
CREATE TABLE ops.ConsentRecord (
  Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ConsentRecord PRIMARY KEY,
  ApplicantAccountId int NOT NULL CONSTRAINT FK_ConsentRecord_Account REFERENCES [identity].ApplicantAccount (Id),
  Kind varchar(30) NOT NULL,
  Granted bit NOT NULL,
  Detail nvarchar(200) NOT NULL,
  At datetime2(3) NOT NULL
);
GO
CREATE INDEX IX_ConsentRecord_Account ON ops.ConsentRecord (ApplicantAccountId, At);
GO
CREATE TABLE ops.RetentionSetting (
  Id tinyint NOT NULL CONSTRAINT PK_RetentionSetting PRIMARY KEY,
  IsEnabled bit NOT NULL,
  UnsuccessfulMonths smallint NOT NULL,
  InactiveAccountMonths smallint NOT NULL,
  LastRunAt datetime2(3) NULL,
  LastRunCount int NOT NULL,
  UpdatedAt datetime2(3) NOT NULL,
  UpdatedByStaffId int NULL
);
GO
INSERT INTO ops.RetentionSetting (Id, IsEnabled, UnsuccessfulMonths, InactiveAccountMonths, LastRunCount, UpdatedAt) VALUES (1, 1, 24, 36, 0, SYSUTCDATETIME());
GO
/* Applications already made carry their declaration as a consent record. */
INSERT INTO ops.ConsentRecord (ApplicantAccountId, Kind, Granted, Detail, At)
SELECT ApplicantAccountId, 'declaration', 1, Code, DeclarationAcceptedAt FROM hiring.JobApplication;
GO
/* Anonymised accounts are marked so they are not anonymised again. */
ALTER TABLE [identity].ApplicantAccount ADD AnonymisedAt datetime2(3) NULL;
GO
ALTER TABLE hiring.JobApplication ADD AnonymisedAt datetime2(3) NULL;
GO
