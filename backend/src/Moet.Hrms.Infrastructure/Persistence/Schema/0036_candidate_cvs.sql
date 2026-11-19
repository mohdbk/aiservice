/* A candidate's own CVs, kept with their account (not only with applications): several per person, one the default.
   Each is read once (text, phone, a structured profile) so applying can be filled in from it and HR can find people in
   the talent pool by their CV without them applying. Deleted with the account. */
CREATE TABLE hiring.CandidateCv (
    Id                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CandidateCv PRIMARY KEY,
    ApplicantAccountId int              NOT NULL CONSTRAINT FK_CandidateCv_Account REFERENCES [identity].ApplicantAccount (Id) ON DELETE CASCADE,
    StoredFileId       uniqueidentifier NOT NULL CONSTRAINT FK_CandidateCv_File REFERENCES ops.StoredFile (Id),
    FileName           nvarchar(200)    NOT NULL,
    SizeBytes          bigint           NOT NULL,
    Sha256             char(64)         NOT NULL,
    IsDefault          bit              NOT NULL,
    Status             varchar(12)      NOT NULL CONSTRAINT CK_CandidateCv_Status CHECK (Status IN ('Pending', 'Ready', 'TextOnly', 'Unreadable')),
    Phone              nvarchar(40)     NOT NULL CONSTRAINT DF_CandidateCv_Phone DEFAULT (N''),
    YearsOfExperience  decimal(4,1)     NULL,
    Summary            nvarchar(2000)   NOT NULL CONSTRAINT DF_CandidateCv_Summary DEFAULT (N''),
    ProfileJson        nvarchar(max)    NULL CONSTRAINT CK_CandidateCv_ProfileJson CHECK (ProfileJson IS NULL OR ISJSON(ProfileJson) = 1),
    [Text]             nvarchar(max)    NOT NULL CONSTRAINT DF_CandidateCv_Text DEFAULT (N''),
    UploadedByStaffId  int              NULL,
    UploadedAt         datetime2(0)     NOT NULL,
    ReadAt             datetime2(0)     NULL
);
CREATE INDEX IX_CandidateCv_Account ON hiring.CandidateCv (ApplicantAccountId, IsDefault);
GO
