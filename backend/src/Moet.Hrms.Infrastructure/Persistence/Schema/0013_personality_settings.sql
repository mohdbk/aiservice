/* HR's personality settings: the workplace Big Five band cut-offs (one row) and their own notes for any of the 16 types. */
CREATE TABLE assessment.PersonalitySetting (
  Id tinyint NOT NULL CONSTRAINT PK_PersonalitySetting PRIMARY KEY,
  BandCutoffs varchar(40) NOT NULL CONSTRAINT DF_PersonalitySetting_Bands DEFAULT ('6,11,16,21'),
  UpdatedAt datetime2(3) NOT NULL,
  UpdatedByStaffId int NULL
);
GO
INSERT INTO assessment.PersonalitySetting (Id, BandCutoffs, UpdatedAt) VALUES (1, '6,11,16,21', SYSUTCDATETIME());
GO
CREATE TABLE assessment.TypeNote (
  Type char(4) NOT NULL CONSTRAINT PK_TypeNote PRIMARY KEY,
  NameAr nvarchar(100) NOT NULL,
  NameEn nvarchar(100) NOT NULL,
  StrengthsAr nvarchar(2000) NOT NULL,
  StrengthsEn nvarchar(2000) NOT NULL,
  RisksAr nvarchar(2000) NOT NULL,
  RisksEn nvarchar(2000) NOT NULL,
  UpdatedAt datetime2(3) NOT NULL,
  UpdatedByStaffId int NULL
);
GO
