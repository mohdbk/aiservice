/* Hand-off to the HR system when an offer is accepted: the settings (one row) and, on each offer, when it got there. */
CREATE TABLE ops.HandOffSetting (
  Id tinyint NOT NULL CONSTRAINT PK_HandOffSetting PRIMARY KEY,
  IsEnabled bit NOT NULL,
  Url nvarchar(500) NOT NULL,
  SecretProtected nvarchar(1000) NOT NULL,
  IncludeCv bit NOT NULL,
  UpdatedAt datetime2(3) NOT NULL,
  UpdatedByStaffId int NULL
);
GO
INSERT INTO ops.HandOffSetting (Id, IsEnabled, Url, SecretProtected, IncludeCv, UpdatedAt) VALUES (1, 0, N'', N'', 0, SYSUTCDATETIME());
GO
ALTER TABLE hiring.JobOffer ADD
  HandedOffAt datetime2(3) NULL,
  HandOffError nvarchar(1000) NOT NULL CONSTRAINT DF_JobOffer_HandOffError DEFAULT (N'');
GO
