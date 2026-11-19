/* A CV uploaded on the sign-up page before the account exists: read at once so the form can be filled in from it (name,
   email, phone), then moved into the new account's CV library when the person signs up. Drafts nobody signed up with
   are deleted, with their file, a day after they were uploaded. */
CREATE TABLE hiring.CvDraft (
    Id                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CvDraft PRIMARY KEY,
    Token              uniqueidentifier NOT NULL CONSTRAINT UQ_CvDraft_Token UNIQUE,
    StoredFileId       uniqueidentifier NOT NULL CONSTRAINT FK_CvDraft_File REFERENCES ops.StoredFile (Id),
    FileName           nvarchar(200)    NOT NULL,
    SizeBytes          bigint           NOT NULL,
    Sha256             char(64)         NOT NULL,
    Status             varchar(12)      NOT NULL CONSTRAINT CK_CvDraft_Status CHECK (Status IN ('Pending', 'Ready', 'TextOnly', 'Unreadable')),
    FullName           nvarchar(140)    NOT NULL CONSTRAINT DF_CvDraft_FullName DEFAULT (N''),
    Email              nvarchar(200)    NOT NULL CONSTRAINT DF_CvDraft_Email DEFAULT (N''),
    Phone              nvarchar(40)     NOT NULL CONSTRAINT DF_CvDraft_Phone DEFAULT (N''),
    YearsOfExperience  decimal(4,1)     NULL,
    Summary            nvarchar(2000)   NOT NULL CONSTRAINT DF_CvDraft_Summary DEFAULT (N''),
    ProfileJson        nvarchar(max)    NULL CONSTRAINT CK_CvDraft_ProfileJson CHECK (ProfileJson IS NULL OR ISJSON(ProfileJson) = 1),
    [Text]             nvarchar(max)    NOT NULL CONSTRAINT DF_CvDraft_Text DEFAULT (N''),
    CreatedAt          datetime2(0)     NOT NULL,
    ExpiresAt          datetime2(0)     NOT NULL,
    ReadAt             datetime2(0)     NULL
);
CREATE INDEX IX_CvDraft_Expires ON hiring.CvDraft (ExpiresAt);
GO
