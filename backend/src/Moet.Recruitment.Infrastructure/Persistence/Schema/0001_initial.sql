/* MoET Recruitment: initial schema (SQL Server 2019 or later).
   Conventions: one schema per area; singular PascalCase tables; surrogate key Id; foreign keys named <Table>Id;
   constraint names PK_, FK_, UQ_, CK_, IX_, DF_; all times UTC (datetime2(0)); text nvarchar; codes varchar.
   The original language of translatable content is in SourceLanguageCode; other languages live in <Table>Translation.
   Each script runs once inside a transaction and is recorded in ops.SchemaVersion. GO separates batches. */

CREATE SCHEMA core;
GO
CREATE SCHEMA [identity];
GO
CREATE SCHEMA hiring;
GO
CREATE SCHEMA assessment;
GO
CREATE SCHEMA ops;
GO

/* ===================== core ===================== */
CREATE TABLE core.Language (
    Code            varchar(10)   NOT NULL CONSTRAINT PK_Language PRIMARY KEY,
    NativeName      nvarchar(60)  NOT NULL,
    EnglishName     nvarchar(60)  NOT NULL,
    IsRtl           bit           NOT NULL CONSTRAINT DF_Language_IsRtl DEFAULT (0),
    IsEnabled       bit           NOT NULL CONSTRAINT DF_Language_IsEnabled DEFAULT (1),
    SortOrder       int           NOT NULL CONSTRAINT DF_Language_SortOrder DEFAULT (0),
    CONSTRAINT CK_Language_Code CHECK (Code NOT LIKE '%[^a-zA-Z-]%' AND LEN(Code) BETWEEN 2 AND 10)
);

CREATE TABLE ops.StoredFile (
    Id              uniqueidentifier NOT NULL CONSTRAINT PK_StoredFile PRIMARY KEY,
    FileName        nvarchar(200) NOT NULL,
    ContentType     varchar(120)  NOT NULL,
    SizeBytes       bigint        NOT NULL CONSTRAINT CK_StoredFile_Size CHECK (SizeBytes >= 0),
    Sha256          char(64)      NOT NULL,
    Content         varbinary(max) NOT NULL,
    CreatedAt       datetime2(0)  NOT NULL
);

CREATE TABLE [identity].StaffUser (
    Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StaffUser PRIMARY KEY,
    Email           nvarchar(254) NOT NULL CONSTRAINT UQ_StaffUser_Email UNIQUE,
    FullName        nvarchar(140) NOT NULL,
    PasswordHash    varchar(400)  NOT NULL,
    Role            varchar(30)   NOT NULL CONSTRAINT CK_StaffUser_Role CHECK (Role IN ('Recruiter', 'RecruitmentAdmin', 'Administrator')),
    IsEnabled       bit           NOT NULL CONSTRAINT DF_StaffUser_IsEnabled DEFAULT (1),
    SessionVersion  int           NOT NULL CONSTRAINT DF_StaffUser_SessionVersion DEFAULT (0),
    FailedSignIns   int           NOT NULL CONSTRAINT DF_StaffUser_FailedSignIns DEFAULT (0),
    LockedUntil     datetime2(0)  NULL,
    LastSignInAt    datetime2(0)  NULL,
    CreatedAt       datetime2(0)  NOT NULL,
    UpdatedAt       datetime2(0)  NOT NULL
);

CREATE TABLE core.PortalSetting (
    Id                       tinyint       NOT NULL CONSTRAINT PK_PortalSetting PRIMARY KEY CONSTRAINT CK_PortalSetting_Single CHECK (Id = 1),
    MainSiteUrl              nvarchar(300) NOT NULL CONSTRAINT DF_PortalSetting_MainSiteUrl DEFAULT (N'https://www.moet.gov.ae'),
    PortalUrl                nvarchar(300) NOT NULL CONSTRAINT DF_PortalSetting_PortalUrl DEFAULT (N''),
    DefaultLanguageCode      varchar(10)   NOT NULL CONSTRAINT FK_PortalSetting_Language REFERENCES core.Language (Code),
    AllowSignup              bit           NOT NULL CONSTRAINT DF_PortalSetting_AllowSignup DEFAULT (1),
    RequireEmailVerification bit           NOT NULL CONSTRAINT DF_PortalSetting_RequireEmailVerification DEFAULT (1),
    MaxCvMb                  tinyint       NOT NULL CONSTRAINT DF_PortalSetting_MaxCvMb DEFAULT (5) CONSTRAINT CK_PortalSetting_MaxCvMb CHECK (MaxCvMb BETWEEN 1 AND 50),
    LogoFileId               uniqueidentifier NULL CONSTRAINT FK_PortalSetting_LogoFile REFERENCES ops.StoredFile (Id),
    LogoDarkFileId           uniqueidentifier NULL CONSTRAINT FK_PortalSetting_LogoDarkFile REFERENCES ops.StoredFile (Id),
    AutoScoreApplications    bit           NOT NULL CONSTRAINT DF_PortalSetting_AutoScore DEFAULT (1),
    AssistantEnabled         bit           NOT NULL CONSTRAINT DF_PortalSetting_AssistantEnabled DEFAULT (1),
    UpdatedAt                datetime2(0)  NOT NULL,
    UpdatedByStaffId         int           NULL CONSTRAINT FK_PortalSetting_UpdatedBy REFERENCES [identity].StaffUser (Id)
);

CREATE TABLE core.PortalText (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PortalText PRIMARY KEY,
    LanguageCode      varchar(10)   NOT NULL CONSTRAINT FK_PortalText_Language REFERENCES core.Language (Code),
    TextKey           varchar(120)  NOT NULL,
    [Text]            nvarchar(max) NOT NULL,
    ReviewStatus      varchar(10)   NOT NULL CONSTRAINT CK_PortalText_ReviewStatus CHECK (ReviewStatus IN ('Suggested', 'Approved')),
    SuggestedHash     char(16)      NULL,
    ApprovedByStaffId int           NULL CONSTRAINT FK_PortalText_ApprovedBy REFERENCES [identity].StaffUser (Id),
    ApprovedAt        datetime2(0)  NULL,
    UpdatedAt         datetime2(0)  NOT NULL,
    CONSTRAINT UQ_PortalText_Language_Key UNIQUE (LanguageCode, TextKey)
);

CREATE TABLE core.Department (
    Id                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Department PRIMARY KEY,
    Name               nvarchar(140) NOT NULL,
    SourceLanguageCode varchar(10)   NOT NULL CONSTRAINT FK_Department_Language REFERENCES core.Language (Code),
    IsActive           bit           NOT NULL CONSTRAINT DF_Department_IsActive DEFAULT (1),
    CONSTRAINT UQ_Department_Name UNIQUE (Name)
);

CREATE TABLE core.DepartmentTranslation (
    DepartmentId   int          NOT NULL CONSTRAINT FK_DepartmentTranslation_Department REFERENCES core.Department (Id) ON DELETE CASCADE,
    LanguageCode   varchar(10)  NOT NULL CONSTRAINT FK_DepartmentTranslation_Language REFERENCES core.Language (Code),
    Name           nvarchar(140) NOT NULL,
    CONSTRAINT PK_DepartmentTranslation PRIMARY KEY (DepartmentId, LanguageCode)
);

/* ===================== identity ===================== */
CREATE TABLE [identity].ApplicantAccount (
    Id                    int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ApplicantAccount PRIMARY KEY,
    Email                 nvarchar(254) NOT NULL CONSTRAINT UQ_ApplicantAccount_Email UNIQUE,
    FullName              nvarchar(140) NOT NULL,
    PasswordHash          varchar(400)  NOT NULL,
    IsEmailVerified       bit           NOT NULL CONSTRAINT DF_ApplicantAccount_IsEmailVerified DEFAULT (0),
    SessionVersion        int           NOT NULL CONSTRAINT DF_ApplicantAccount_SessionVersion DEFAULT (0),
    PreferredLanguageCode varchar(10)   NULL CONSTRAINT FK_ApplicantAccount_Language REFERENCES core.Language (Code),
    IsEnabled             bit           NOT NULL CONSTRAINT DF_ApplicantAccount_IsEnabled DEFAULT (1),
    FailedSignIns         int           NOT NULL CONSTRAINT DF_ApplicantAccount_FailedSignIns DEFAULT (0),
    LockedUntil           datetime2(0)  NULL,
    LastSignInAt          datetime2(0)  NULL,
    CreatedAt             datetime2(0)  NOT NULL
);

CREATE TABLE [identity].AccountToken (
    Id                 bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountToken PRIMARY KEY,
    ApplicantAccountId int           NOT NULL CONSTRAINT FK_AccountToken_ApplicantAccount REFERENCES [identity].ApplicantAccount (Id) ON DELETE CASCADE,
    Purpose            varchar(20)   NOT NULL CONSTRAINT CK_AccountToken_Purpose CHECK (Purpose IN ('VerifyEmail', 'ResetPassword')),
    TokenHash          char(64)      NOT NULL CONSTRAINT UQ_AccountToken_TokenHash UNIQUE,
    LanguageCode       varchar(10)   NOT NULL,
    NextPath           nvarchar(300) NOT NULL CONSTRAINT DF_AccountToken_NextPath DEFAULT (N''),
    ExpiresAt          datetime2(0)  NOT NULL,
    UsedAt             datetime2(0)  NULL,
    CreatedAt          datetime2(0)  NOT NULL
);
CREATE INDEX IX_AccountToken_Account_Purpose ON [identity].AccountToken (ApplicantAccountId, Purpose) WHERE UsedAt IS NULL;

/* ===================== assessment (definitions) ===================== */
CREATE TABLE assessment.Question (
    Id                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Question PRIMARY KEY,
    QuestionType       varchar(20)   NOT NULL CONSTRAINT CK_Question_Type CHECK (QuestionType IN ('SingleChoice', 'MultipleChoice', 'TrueFalse', 'ShortAnswer', 'Number', 'Essay', 'FileUpload')),
    [Text]             nvarchar(max) NOT NULL,
    Category           nvarchar(100) NOT NULL CONSTRAINT DF_Question_Category DEFAULT (N''),
    JobFamily          nvarchar(100) NOT NULL CONSTRAINT DF_Question_JobFamily DEFAULT (N''),
    Difficulty         varchar(10)   NOT NULL CONSTRAINT CK_Question_Difficulty CHECK (Difficulty IN ('Easy', 'Medium', 'Hard')),
    Points             decimal(6,2)  NOT NULL CONSTRAINT CK_Question_Points CHECK (Points > 0),
    Tolerance          decimal(18,4) NOT NULL CONSTRAINT DF_Question_Tolerance DEFAULT (0),
    Rubric             nvarchar(max) NOT NULL CONSTRAINT DF_Question_Rubric DEFAULT (N''),
    MaxWords           int           NOT NULL CONSTRAINT DF_Question_MaxWords DEFAULT (0),
    IsActive           bit           NOT NULL CONSTRAINT DF_Question_IsActive DEFAULT (1),
    SourceLanguageCode varchar(10)   NOT NULL CONSTRAINT FK_Question_Language REFERENCES core.Language (Code),
    CreatedAt          datetime2(0)  NOT NULL,
    UpdatedAt          datetime2(0)  NOT NULL
);
CREATE INDEX IX_Question_Draw ON assessment.Question (IsActive, Category, Difficulty, JobFamily);

CREATE TABLE assessment.QuestionOption (
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_QuestionOption PRIMARY KEY,
    QuestionId int           NOT NULL CONSTRAINT FK_QuestionOption_Question REFERENCES assessment.Question (Id) ON DELETE CASCADE,
    SortOrder  int           NOT NULL,
    [Text]     nvarchar(500) NOT NULL,
    IsCorrect  bit           NOT NULL CONSTRAINT DF_QuestionOption_IsCorrect DEFAULT (0)
);
CREATE INDEX IX_QuestionOption_Question ON assessment.QuestionOption (QuestionId, SortOrder);

CREATE TABLE assessment.QuestionAcceptedAnswer (
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_QuestionAcceptedAnswer PRIMARY KEY,
    QuestionId int           NOT NULL CONSTRAINT FK_QuestionAcceptedAnswer_Question REFERENCES assessment.Question (Id) ON DELETE CASCADE,
    SortOrder  int           NOT NULL,
    Answer     nvarchar(500) NOT NULL
);

CREATE TABLE assessment.QuestionTranslation (
    QuestionId        int           NOT NULL CONSTRAINT FK_QuestionTranslation_Question REFERENCES assessment.Question (Id) ON DELETE CASCADE,
    LanguageCode      varchar(10)   NOT NULL CONSTRAINT FK_QuestionTranslation_Language REFERENCES core.Language (Code),
    [Text]            nvarchar(max) NOT NULL,
    ReviewStatus      varchar(10)   NOT NULL CONSTRAINT CK_QuestionTranslation_ReviewStatus CHECK (ReviewStatus IN ('Suggested', 'Approved')),
    SuggestedHash     char(16)      NULL,
    ApprovedByStaffId int           NULL CONSTRAINT FK_QuestionTranslation_ApprovedBy REFERENCES [identity].StaffUser (Id),
    ApprovedAt        datetime2(0)  NULL,
    CONSTRAINT PK_QuestionTranslation PRIMARY KEY (QuestionId, LanguageCode)
);

CREATE TABLE assessment.QuestionOptionTranslation (
    QuestionOptionId int           NOT NULL CONSTRAINT FK_QuestionOptionTranslation_Option REFERENCES assessment.QuestionOption (Id) ON DELETE CASCADE,
    LanguageCode     varchar(10)   NOT NULL CONSTRAINT FK_QuestionOptionTranslation_Language REFERENCES core.Language (Code),
    [Text]           nvarchar(500) NOT NULL,
    CONSTRAINT PK_QuestionOptionTranslation PRIMARY KEY (QuestionOptionId, LanguageCode)
);

CREATE TABLE assessment.Assessment (
    Id                   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Assessment PRIMARY KEY,
    Title                nvarchar(140) NOT NULL,
    Instructions         nvarchar(max) NOT NULL CONSTRAINT DF_Assessment_Instructions DEFAULT (N''),
    AssessmentType       varchar(30)   NOT NULL CONSTRAINT CK_Assessment_Type CHECK (AssessmentType IN ('KnowledgeTest', 'LanguageCheck', 'SituationalJudgement', 'WorkSample', 'Aptitude', 'Other')),
    Status               varchar(10)   NOT NULL CONSTRAINT CK_Assessment_Status CHECK (Status IN ('Draft', 'Published', 'Archived')),
    PassMark             decimal(5,2)  NOT NULL CONSTRAINT CK_Assessment_PassMark CHECK (PassMark BETWEEN 0 AND 100),
    MaxAttempts          tinyint       NOT NULL CONSTRAINT CK_Assessment_MaxAttempts CHECK (MaxAttempts >= 1),
    ScorePolicy          varchar(10)   NOT NULL CONSTRAINT CK_Assessment_ScorePolicy CHECK (ScorePolicy IN ('Highest', 'Latest')),
    ShuffleQuestions     bit           NOT NULL,
    ShuffleOptions       bit           NOT NULL,
    ShowScoreToCandidate bit           NOT NULL,
    DefaultDeadlineDays  tinyint       NOT NULL CONSTRAINT CK_Assessment_DeadlineDays CHECK (DefaultDeadlineDays BETWEEN 1 AND 90),
    SourceLanguageCode   varchar(10)   NOT NULL CONSTRAINT FK_Assessment_Language REFERENCES core.Language (Code),
    CreatedAt            datetime2(0)  NOT NULL,
    UpdatedAt            datetime2(0)  NOT NULL
);

CREATE TABLE assessment.Section (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Section PRIMARY KEY,
    AssessmentId int           NOT NULL CONSTRAINT FK_Section_Assessment REFERENCES assessment.Assessment (Id) ON DELETE CASCADE,
    SortOrder    int           NOT NULL,
    Title        nvarchar(140) NOT NULL,
    Instructions nvarchar(2000) NOT NULL CONSTRAINT DF_Section_Instructions DEFAULT (N''),
    Minutes      int           NOT NULL CONSTRAINT CK_Section_Minutes CHECK (Minutes BETWEEN 0 AND 600),
    PassMark     decimal(5,2)  NOT NULL CONSTRAINT CK_Section_PassMark CHECK (PassMark BETWEEN 0 AND 100)
);
CREATE INDEX IX_Section_Assessment ON assessment.Section (AssessmentId, SortOrder);

CREATE TABLE assessment.SectionItem (
    Id             int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SectionItem PRIMARY KEY,
    SectionId      int           NOT NULL CONSTRAINT FK_SectionItem_Section REFERENCES assessment.Section (Id) ON DELETE CASCADE,
    SortOrder      int           NOT NULL,
    QuestionId     int           NULL CONSTRAINT FK_SectionItem_Question REFERENCES assessment.Question (Id),
    DrawCategory   nvarchar(100) NOT NULL CONSTRAINT DF_SectionItem_DrawCategory DEFAULT (N''),
    DrawDifficulty varchar(10)   NOT NULL CONSTRAINT DF_SectionItem_DrawDifficulty DEFAULT (''),
    DrawJobFamily  nvarchar(100) NOT NULL CONSTRAINT DF_SectionItem_DrawJobFamily DEFAULT (N''),
    DrawCount      int           NOT NULL CONSTRAINT DF_SectionItem_DrawCount DEFAULT (0),
    PointsOverride decimal(6,2)  NULL,
    CONSTRAINT CK_SectionItem_QuestionOrDraw CHECK (QuestionId IS NOT NULL OR DrawCount > 0)
);

CREATE TABLE assessment.AssessmentTranslation (
    AssessmentId      int           NOT NULL CONSTRAINT FK_AssessmentTranslation_Assessment REFERENCES assessment.Assessment (Id) ON DELETE CASCADE,
    LanguageCode      varchar(10)   NOT NULL CONSTRAINT FK_AssessmentTranslation_Language REFERENCES core.Language (Code),
    Title             nvarchar(140) NOT NULL,
    Instructions      nvarchar(max) NOT NULL,
    ReviewStatus      varchar(10)   NOT NULL CONSTRAINT CK_AssessmentTranslation_ReviewStatus CHECK (ReviewStatus IN ('Suggested', 'Approved')),
    SuggestedHash     char(16)      NULL,
    ApprovedByStaffId int           NULL CONSTRAINT FK_AssessmentTranslation_ApprovedBy REFERENCES [identity].StaffUser (Id),
    ApprovedAt        datetime2(0)  NULL,
    CONSTRAINT PK_AssessmentTranslation PRIMARY KEY (AssessmentId, LanguageCode)
);

CREATE TABLE assessment.SectionTranslation (
    SectionId    int            NOT NULL CONSTRAINT FK_SectionTranslation_Section REFERENCES assessment.Section (Id) ON DELETE CASCADE,
    LanguageCode varchar(10)    NOT NULL CONSTRAINT FK_SectionTranslation_Language REFERENCES core.Language (Code),
    Title        nvarchar(140)  NOT NULL,
    Instructions nvarchar(2000) NOT NULL,
    CONSTRAINT PK_SectionTranslation PRIMARY KEY (SectionId, LanguageCode)
);

/* ===================== hiring ===================== */
CREATE TABLE hiring.JobOpening (
    Id                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_JobOpening PRIMARY KEY,
    Code               varchar(20)   NOT NULL CONSTRAINT UQ_JobOpening_Code UNIQUE,
    Title              nvarchar(140) NOT NULL,
    Description        nvarchar(max) NOT NULL CONSTRAINT DF_JobOpening_Description DEFAULT (N''),
    SourceLanguageCode varchar(10)   NOT NULL CONSTRAINT FK_JobOpening_Language REFERENCES core.Language (Code),
    DepartmentId       int           NULL CONSTRAINT FK_JobOpening_Department REFERENCES core.Department (Id),
    Location           nvarchar(140) NOT NULL CONSTRAINT DF_JobOpening_Location DEFAULT (N''),
    EmploymentType     varchar(20)   NOT NULL CONSTRAINT CK_JobOpening_EmploymentType CHECK (EmploymentType IN ('FullTime', 'PartTime', 'Contract', 'Temporary', 'Internship')),
    Status             varchar(10)   NOT NULL CONSTRAINT CK_JobOpening_Status CHECK (Status IN ('Draft', 'Open', 'Closed')),
    IsPublished        bit           NOT NULL CONSTRAINT DF_JobOpening_IsPublished DEFAULT (0),
    PostedOn           date          NULL,
    ClosesOn           date          NULL,
    SalaryMin          decimal(12,2) NULL,
    SalaryMax          decimal(12,2) NULL,
    SalaryCurrency     char(3)       NOT NULL CONSTRAINT DF_JobOpening_SalaryCurrency DEFAULT ('AED'),
    SalaryPeriod       varchar(10)   NOT NULL CONSTRAINT DF_JobOpening_SalaryPeriod DEFAULT ('Month') CONSTRAINT CK_JobOpening_SalaryPeriod CHECK (SalaryPeriod IN ('Month', 'Year')),
    ShowSalary         bit           NOT NULL CONSTRAINT DF_JobOpening_ShowSalary DEFAULT (0),
    CriteriaVersion    char(16)      NULL,
    CreatedAt          datetime2(0)  NOT NULL,
    CreatedByStaffId   int           NULL CONSTRAINT FK_JobOpening_CreatedBy REFERENCES [identity].StaffUser (Id),
    UpdatedAt          datetime2(0)  NOT NULL,
    UpdatedByStaffId   int           NULL CONSTRAINT FK_JobOpening_UpdatedBy REFERENCES [identity].StaffUser (Id),
    RowVersion         rowversion    NOT NULL,
    CONSTRAINT CK_JobOpening_Salary CHECK (SalaryMin IS NULL OR SalaryMax IS NULL OR SalaryMin <= SalaryMax),
    CONSTRAINT CK_JobOpening_Dates CHECK (ClosesOn IS NULL OR PostedOn IS NULL OR ClosesOn >= PostedOn)
);
CREATE INDEX IX_JobOpening_Listing ON hiring.JobOpening (IsPublished, Status, PostedOn DESC);

CREATE TABLE hiring.JobOpeningTranslation (
    JobOpeningId      int           NOT NULL CONSTRAINT FK_JobOpeningTranslation_JobOpening REFERENCES hiring.JobOpening (Id) ON DELETE CASCADE,
    LanguageCode      varchar(10)   NOT NULL CONSTRAINT FK_JobOpeningTranslation_Language REFERENCES core.Language (Code),
    Title             nvarchar(140) NOT NULL,
    Description       nvarchar(max) NOT NULL,
    ReviewStatus      varchar(10)   NOT NULL CONSTRAINT CK_JobOpeningTranslation_ReviewStatus CHECK (ReviewStatus IN ('Suggested', 'Approved')),
    SuggestedHash     char(16)      NULL,
    ApprovedByStaffId int           NULL CONSTRAINT FK_JobOpeningTranslation_ApprovedBy REFERENCES [identity].StaffUser (Id),
    ApprovedAt        datetime2(0)  NULL,
    CONSTRAINT PK_JobOpeningTranslation PRIMARY KEY (JobOpeningId, LanguageCode)
);

CREATE TABLE hiring.JobCriterion (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_JobCriterion PRIMARY KEY,
    JobOpeningId int           NOT NULL CONSTRAINT FK_JobCriterion_JobOpening REFERENCES hiring.JobOpening (Id) ON DELETE CASCADE,
    SortOrder    int           NOT NULL,
    Criterion    nvarchar(300) NOT NULL,
    Weight       decimal(5,2)  NOT NULL CONSTRAINT CK_JobCriterion_Weight CHECK (Weight > 0),
    IsMustHave   bit           NOT NULL
);
CREATE INDEX IX_JobCriterion_JobOpening ON hiring.JobCriterion (JobOpeningId, SortOrder);

CREATE TABLE hiring.ScreeningQuestion (
    Id                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ScreeningQuestion PRIMARY KEY,
    [Text]             nvarchar(1000) NOT NULL,
    AnswerType         varchar(20)   NOT NULL CONSTRAINT CK_ScreeningQuestion_AnswerType CHECK (AnswerType IN ('YesNo', 'SingleChoice', 'MultipleChoice', 'Number', 'Date', 'ShortText', 'LongText')),
    Category           nvarchar(100) NOT NULL CONSTRAINT DF_ScreeningQuestion_Category DEFAULT (N''),
    IsActive           bit           NOT NULL CONSTRAINT DF_ScreeningQuestion_IsActive DEFAULT (1),
    SourceLanguageCode varchar(10)   NOT NULL CONSTRAINT FK_ScreeningQuestion_Language REFERENCES core.Language (Code),
    CreatedAt          datetime2(0)  NOT NULL,
    UpdatedAt          datetime2(0)  NOT NULL
);

CREATE TABLE hiring.ScreeningQuestionOption (
    Id                  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ScreeningQuestionOption PRIMARY KEY,
    ScreeningQuestionId int           NOT NULL CONSTRAINT FK_ScreeningQuestionOption_Question REFERENCES hiring.ScreeningQuestion (Id) ON DELETE CASCADE,
    SortOrder           int           NOT NULL,
    [Text]              nvarchar(300) NOT NULL
);

CREATE TABLE hiring.ScreeningQuestionTranslation (
    ScreeningQuestionId int            NOT NULL CONSTRAINT FK_ScreeningQuestionTranslation_Question REFERENCES hiring.ScreeningQuestion (Id) ON DELETE CASCADE,
    LanguageCode        varchar(10)    NOT NULL CONSTRAINT FK_ScreeningQuestionTranslation_Language REFERENCES core.Language (Code),
    [Text]              nvarchar(1000) NOT NULL,
    ReviewStatus        varchar(10)    NOT NULL CONSTRAINT CK_ScreeningQuestionTranslation_ReviewStatus CHECK (ReviewStatus IN ('Suggested', 'Approved')),
    SuggestedHash       char(16)       NULL,
    ApprovedByStaffId   int            NULL CONSTRAINT FK_ScreeningQuestionTranslation_ApprovedBy REFERENCES [identity].StaffUser (Id),
    ApprovedAt          datetime2(0)   NULL,
    CONSTRAINT PK_ScreeningQuestionTranslation PRIMARY KEY (ScreeningQuestionId, LanguageCode)
);

CREATE TABLE hiring.ScreeningQuestionOptionTranslation (
    ScreeningQuestionOptionId int           NOT NULL CONSTRAINT FK_ScreeningQuestionOptionTranslation_Option REFERENCES hiring.ScreeningQuestionOption (Id) ON DELETE CASCADE,
    LanguageCode              varchar(10)   NOT NULL CONSTRAINT FK_ScreeningQuestionOptionTranslation_Language REFERENCES core.Language (Code),
    [Text]                    nvarchar(300) NOT NULL,
    CONSTRAINT PK_ScreeningQuestionOptionTranslation PRIMARY KEY (ScreeningQuestionOptionId, LanguageCode)
);

CREATE TABLE hiring.JobScreeningQuestion (
    JobOpeningId        int           NOT NULL CONSTRAINT FK_JobScreeningQuestion_JobOpening REFERENCES hiring.JobOpening (Id) ON DELETE CASCADE,
    ScreeningQuestionId int           NOT NULL CONSTRAINT FK_JobScreeningQuestion_Question REFERENCES hiring.ScreeningQuestion (Id),
    SortOrder           int           NOT NULL,
    IsRequired          bit           NOT NULL,
    RequirementRule     varchar(10)   NULL CONSTRAINT CK_JobScreeningQuestion_Rule CHECK (RequirementRule IN ('MustBe', 'AtLeast', 'AtMost')),
    ExpectedValue       nvarchar(300) NULL,
    CONSTRAINT PK_JobScreeningQuestion PRIMARY KEY (JobOpeningId, ScreeningQuestionId),
    CONSTRAINT CK_JobScreeningQuestion_Expected CHECK (RequirementRule IS NULL OR ExpectedValue IS NOT NULL)
);

CREATE TABLE hiring.JobAssessmentStage (
    JobOpeningId int        NOT NULL CONSTRAINT FK_JobAssessmentStage_JobOpening REFERENCES hiring.JobOpening (Id) ON DELETE CASCADE,
    AssessmentId int        NOT NULL CONSTRAINT FK_JobAssessmentStage_Assessment REFERENCES assessment.Assessment (Id),
    SortOrder    int        NOT NULL,
    SendWhen     varchar(10) NOT NULL CONSTRAINT CK_JobAssessmentStage_SendWhen CHECK (SendWhen IN ('OnApply', 'Manual')),
    DeadlineDays tinyint    NOT NULL CONSTRAINT CK_JobAssessmentStage_DeadlineDays CHECK (DeadlineDays BETWEEN 1 AND 90),
    CONSTRAINT PK_JobAssessmentStage PRIMARY KEY (JobOpeningId, AssessmentId)
);

CREATE TABLE hiring.JobApplication (
    Id                    int IDENTITY(1,1) NOT NULL CONSTRAINT PK_JobApplication PRIMARY KEY,
    Code                  varchar(20)    NOT NULL CONSTRAINT UQ_JobApplication_Code UNIQUE,
    JobOpeningId          int            NOT NULL CONSTRAINT FK_JobApplication_JobOpening REFERENCES hiring.JobOpening (Id),
    ApplicantAccountId    int            NOT NULL CONSTRAINT FK_JobApplication_ApplicantAccount REFERENCES [identity].ApplicantAccount (Id),
    FullName              nvarchar(140)  NOT NULL,
    Email                 nvarchar(254)  NOT NULL,
    Phone                 varchar(30)    NOT NULL,
    Country               nvarchar(100)  NOT NULL CONSTRAINT DF_JobApplication_Country DEFAULT (N''),
    CoverNote             nvarchar(4000) NOT NULL CONSTRAINT DF_JobApplication_CoverNote DEFAULT (N''),
    Status                varchar(10)    NOT NULL CONSTRAINT CK_JobApplication_Status CHECK (Status IN ('Open', 'Replied', 'Hold', 'Accepted', 'Rejected')),
    Source                varchar(30)    NOT NULL CONSTRAINT DF_JobApplication_Source DEFAULT ('Portal'),
    LanguageCode          varchar(10)    NOT NULL CONSTRAINT FK_JobApplication_Language REFERENCES core.Language (Code),
    DeclarationAcceptedAt datetime2(0)   NOT NULL,
    CvFileId              uniqueidentifier NOT NULL CONSTRAINT FK_JobApplication_CvFile REFERENCES ops.StoredFile (Id),
    ScreeningOutcome      varchar(20)    NOT NULL CONSTRAINT CK_JobApplication_ScreeningOutcome CHECK (ScreeningOutcome IN ('NoQuestions', 'NoRequirements', 'Meets', 'DoesNotMeet')),
    CreatedAt             datetime2(0)   NOT NULL,
    UpdatedAt             datetime2(0)   NOT NULL,
    RowVersion            rowversion     NOT NULL,
    CONSTRAINT UQ_JobApplication_Job_Applicant UNIQUE (JobOpeningId, ApplicantAccountId)
);
CREATE INDEX IX_JobApplication_Applicant ON hiring.JobApplication (ApplicantAccountId, CreatedAt DESC);
CREATE INDEX IX_JobApplication_Pipeline ON hiring.JobApplication (JobOpeningId, Status);

CREATE TABLE hiring.ScreeningAnswer (
    JobApplicationId    int            NOT NULL CONSTRAINT FK_ScreeningAnswer_JobApplication REFERENCES hiring.JobApplication (Id) ON DELETE CASCADE,
    ScreeningQuestionId int            NOT NULL CONSTRAINT FK_ScreeningAnswer_Question REFERENCES hiring.ScreeningQuestion (Id),
    SortOrder           int            NOT NULL,
    QuestionText        nvarchar(1000) NOT NULL,
    AnswerText          nvarchar(4000) NOT NULL,
    MeetsRequirement    bit            NULL,
    CONSTRAINT PK_ScreeningAnswer PRIMARY KEY (JobApplicationId, ScreeningQuestionId)
);

CREATE TABLE hiring.CvAnalysis (
    JobApplicationId  int            NOT NULL CONSTRAINT PK_CvAnalysis PRIMARY KEY CONSTRAINT FK_CvAnalysis_JobApplication REFERENCES hiring.JobApplication (Id) ON DELETE CASCADE,
    Status            varchar(12)    NOT NULL CONSTRAINT CK_CvAnalysis_Status CHECK (Status IN ('Pending', 'Processing', 'Scored', 'Failed', 'Skipped')),
    Score             decimal(5,1)   NULL CONSTRAINT CK_CvAnalysis_Score CHECK (Score BETWEEN 0 AND 100),
    Summary           nvarchar(2000) NOT NULL CONSTRAINT DF_CvAnalysis_Summary DEFAULT (N''),
    SummaryArabic     nvarchar(2000) NOT NULL CONSTRAINT DF_CvAnalysis_SummaryArabic DEFAULT (N''),
    MustHaveGaps      nvarchar(2000) NOT NULL CONSTRAINT DF_CvAnalysis_MustHaveGaps DEFAULT (N''),
    YearsOfExperience decimal(4,1)   NULL,
    CvLanguage        varchar(10)    NOT NULL CONSTRAINT DF_CvAnalysis_CvLanguage DEFAULT (''),
    ReadMethod        varchar(30)    NOT NULL CONSTRAINT DF_CvAnalysis_ReadMethod DEFAULT (''),
    Model             varchar(100)   NOT NULL CONSTRAINT DF_CvAnalysis_Model DEFAULT (''),
    CriteriaVersion   char(16)       NULL,
    ProfileJson       nvarchar(max)  NULL CONSTRAINT CK_CvAnalysis_ProfileJson CHECK (ProfileJson IS NULL OR ISJSON(ProfileJson) = 1),
    Error             nvarchar(500)  NOT NULL CONSTRAINT DF_CvAnalysis_Error DEFAULT (N''),
    ScoredAt          datetime2(0)   NULL,
    UpdatedAt         datetime2(0)   NOT NULL
);

CREATE TABLE hiring.CvCriterionScore (
    JobApplicationId int            NOT NULL CONSTRAINT FK_CvCriterionScore_CvAnalysis REFERENCES hiring.CvAnalysis (JobApplicationId) ON DELETE CASCADE,
    SortOrder        int            NOT NULL,
    Criterion        nvarchar(300)  NOT NULL,
    Weight           decimal(5,2)   NOT NULL,
    IsMustHave       bit            NOT NULL,
    Score            tinyint        NOT NULL CONSTRAINT CK_CvCriterionScore_Score CHECK (Score BETWEEN 0 AND 10),
    Evidence         nvarchar(1000) NOT NULL,
    CONSTRAINT PK_CvCriterionScore PRIMARY KEY (JobApplicationId, SortOrder)
);

CREATE TABLE hiring.ApplicationNote (
    Id                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ApplicationNote PRIMARY KEY,
    JobApplicationId   int            NOT NULL CONSTRAINT FK_ApplicationNote_JobApplication REFERENCES hiring.JobApplication (Id) ON DELETE CASCADE,
    Kind               varchar(12)    NOT NULL CONSTRAINT CK_ApplicationNote_Kind CHECK (Kind IN ('Note', 'Correction', 'System')),
    [Text]             nvarchar(4000) NOT NULL,
    AuthorStaffId      int            NULL CONSTRAINT FK_ApplicationNote_Staff REFERENCES [identity].StaffUser (Id),
    AuthorApplicantId  int            NULL CONSTRAINT FK_ApplicationNote_Applicant REFERENCES [identity].ApplicantAccount (Id),
    CreatedAt          datetime2(0)   NOT NULL
);
CREATE INDEX IX_ApplicationNote_JobApplication ON hiring.ApplicationNote (JobApplicationId, CreatedAt);

CREATE TABLE hiring.InterviewRound (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewRound PRIMARY KEY,
    Name        nvarchar(140)  NOT NULL CONSTRAINT UQ_InterviewRound_Name UNIQUE,
    Description nvarchar(1000) NOT NULL CONSTRAINT DF_InterviewRound_Description DEFAULT (N''),
    IsActive    bit            NOT NULL CONSTRAINT DF_InterviewRound_IsActive DEFAULT (1)
);

CREATE TABLE hiring.InterviewRoundSkill (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewRoundSkill PRIMARY KEY,
    InterviewRoundId int           NOT NULL CONSTRAINT FK_InterviewRoundSkill_Round REFERENCES hiring.InterviewRound (Id) ON DELETE CASCADE,
    SortOrder        int           NOT NULL,
    Skill            nvarchar(140) NOT NULL
);

CREATE TABLE hiring.Interview (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Interview PRIMARY KEY,
    Code             varchar(20)    NOT NULL CONSTRAINT UQ_Interview_Code UNIQUE,
    JobApplicationId int            NOT NULL CONSTRAINT FK_Interview_JobApplication REFERENCES hiring.JobApplication (Id),
    InterviewRoundId int            NOT NULL CONSTRAINT FK_Interview_Round REFERENCES hiring.InterviewRound (Id),
    StartsAt         datetime2(0)   NOT NULL,
    EndsAt           datetime2(0)   NOT NULL,
    Location         nvarchar(300)  NOT NULL CONSTRAINT DF_Interview_Location DEFAULT (N''),
    Status           varchar(12)    NOT NULL CONSTRAINT CK_Interview_Status CHECK (Status IN ('Scheduled', 'UnderReview', 'Cleared', 'Rejected', 'Cancelled')),
    Notes            nvarchar(2000) NOT NULL CONSTRAINT DF_Interview_Notes DEFAULT (N''),
    CreatedAt        datetime2(0)   NOT NULL,
    CreatedByStaffId int            NULL CONSTRAINT FK_Interview_CreatedBy REFERENCES [identity].StaffUser (Id),
    UpdatedAt        datetime2(0)   NOT NULL,
    CONSTRAINT CK_Interview_Times CHECK (EndsAt > StartsAt)
);
CREATE INDEX IX_Interview_StartsAt ON hiring.Interview (StartsAt) INCLUDE (Status);
CREATE INDEX IX_Interview_JobApplication ON hiring.Interview (JobApplicationId);

CREATE TABLE hiring.InterviewPanelist (
    InterviewId int NOT NULL CONSTRAINT FK_InterviewPanelist_Interview REFERENCES hiring.Interview (Id) ON DELETE CASCADE,
    StaffUserId int NOT NULL CONSTRAINT FK_InterviewPanelist_Staff REFERENCES [identity].StaffUser (Id),
    CONSTRAINT PK_InterviewPanelist PRIMARY KEY (InterviewId, StaffUserId)
);
CREATE INDEX IX_InterviewPanelist_Staff ON hiring.InterviewPanelist (StaffUserId);

CREATE TABLE hiring.InterviewFeedback (
    Id             int IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewFeedback PRIMARY KEY,
    InterviewId    int            NOT NULL CONSTRAINT FK_InterviewFeedback_Interview REFERENCES hiring.Interview (Id) ON DELETE CASCADE,
    StaffUserId    int            NOT NULL CONSTRAINT FK_InterviewFeedback_Staff REFERENCES [identity].StaffUser (Id),
    Recommendation varchar(10)    NOT NULL CONSTRAINT CK_InterviewFeedback_Recommendation CHECK (Recommendation IN ('Cleared', 'Rejected')),
    Comments       nvarchar(4000) NOT NULL,
    SubmittedAt    datetime2(0)   NOT NULL,
    CONSTRAINT UQ_InterviewFeedback_Interview_Staff UNIQUE (InterviewId, StaffUserId)
);

CREATE TABLE hiring.InterviewFeedbackRating (
    InterviewFeedbackId int           NOT NULL CONSTRAINT FK_InterviewFeedbackRating_Feedback REFERENCES hiring.InterviewFeedback (Id) ON DELETE CASCADE,
    Skill               nvarchar(140) NOT NULL,
    Rating              tinyint       NOT NULL CONSTRAINT CK_InterviewFeedbackRating_Rating CHECK (Rating BETWEEN 1 AND 5),
    CONSTRAINT PK_InterviewFeedbackRating PRIMARY KEY (InterviewFeedbackId, Skill)
);

CREATE TABLE hiring.JobOffer (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_JobOffer PRIMARY KEY,
    Code             varchar(20)    NOT NULL CONSTRAINT UQ_JobOffer_Code UNIQUE,
    JobApplicationId int            NOT NULL CONSTRAINT FK_JobOffer_JobApplication REFERENCES hiring.JobApplication (Id),
    PositionTitle    nvarchar(140)  NOT NULL,
    OfferDate        date           NOT NULL,
    StartDate        date           NULL,
    MonthlySalary    decimal(12,2)  NULL CONSTRAINT CK_JobOffer_Salary CHECK (MonthlySalary IS NULL OR MonthlySalary >= 0),
    Currency         char(3)        NOT NULL CONSTRAINT DF_JobOffer_Currency DEFAULT ('AED'),
    Terms            nvarchar(max)  NOT NULL CONSTRAINT DF_JobOffer_Terms DEFAULT (N''),
    Status           varchar(10)    NOT NULL CONSTRAINT CK_JobOffer_Status CHECK (Status IN ('Draft', 'Sent', 'Accepted', 'Declined', 'Withdrawn')),
    CreatedAt        datetime2(0)   NOT NULL,
    CreatedByStaffId int            NULL CONSTRAINT FK_JobOffer_CreatedBy REFERENCES [identity].StaffUser (Id),
    UpdatedAt        datetime2(0)   NOT NULL
);
CREATE INDEX IX_JobOffer_JobApplication ON hiring.JobOffer (JobApplicationId);

/* ===================== assessment (candidates) ===================== */
CREATE TABLE assessment.Invitation (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Invitation PRIMARY KEY,
    Code              varchar(20)   NOT NULL CONSTRAINT UQ_Invitation_Code UNIQUE,
    JobApplicationId  int           NOT NULL CONSTRAINT FK_Invitation_JobApplication REFERENCES hiring.JobApplication (Id),
    AssessmentId      int           NOT NULL CONSTRAINT FK_Invitation_Assessment REFERENCES assessment.Assessment (Id),
    Status            varchar(12)   NOT NULL CONSTRAINT CK_Invitation_Status CHECK (Status IN ('Invited', 'InProgress', 'Submitted', 'Marked', 'Expired', 'Cancelled')),
    DeadlineAt        datetime2(0)  NOT NULL,
    AttemptsAllowed   tinyint       NOT NULL CONSTRAINT CK_Invitation_AttemptsAllowed CHECK (AttemptsAllowed >= 1),
    AttemptsUsed      tinyint       NOT NULL CONSTRAINT DF_Invitation_AttemptsUsed DEFAULT (0),
    ExtraTimePercent  tinyint       NOT NULL CONSTRAINT DF_Invitation_ExtraTime DEFAULT (0) CONSTRAINT CK_Invitation_ExtraTime CHECK (ExtraTimePercent <= 200),
    LanguageCode      varchar(10)   NOT NULL CONSTRAINT FK_Invitation_Language REFERENCES core.Language (Code),
    Score             decimal(5,1)  NULL,
    Result            varchar(16)   NULL CONSTRAINT CK_Invitation_Result CHECK (Result IN ('Pass', 'Fail', 'AwaitingMarking')),
    CreatedAt         datetime2(0)  NOT NULL,
    CreatedByStaffId  int           NULL CONSTRAINT FK_Invitation_CreatedBy REFERENCES [identity].StaffUser (Id),
    UpdatedAt         datetime2(0)  NOT NULL,
    CONSTRAINT CK_Invitation_Attempts CHECK (AttemptsUsed <= AttemptsAllowed)
);
CREATE INDEX IX_Invitation_JobApplication ON assessment.Invitation (JobApplicationId);
CREATE INDEX IX_Invitation_Expiry ON assessment.Invitation (Status, DeadlineAt);

CREATE TABLE assessment.Attempt (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Attempt PRIMARY KEY,
    InvitationId     int           NOT NULL CONSTRAINT FK_Attempt_Invitation REFERENCES assessment.Invitation (Id),
    AttemptNo        tinyint       NOT NULL,
    Status           varchar(12)   NOT NULL CONSTRAINT CK_Attempt_Status CHECK (Status IN ('InProgress', 'Submitted', 'Marked')),
    StartedAt        datetime2(0)  NOT NULL,
    SubmittedAt      datetime2(0)  NULL,
    CurrentSectionNo int           NOT NULL CONSTRAINT DF_Attempt_CurrentSectionNo DEFAULT (0),
    SectionStartedAt datetime2(0)  NULL,
    SectionEndsAt    datetime2(0)  NULL,
    Earned           decimal(8,2)  NOT NULL CONSTRAINT DF_Attempt_Earned DEFAULT (0),
    Total            decimal(8,2)  NOT NULL CONSTRAINT DF_Attempt_Total DEFAULT (0),
    [Percent]        decimal(5,1)  NOT NULL CONSTRAINT DF_Attempt_Percent DEFAULT (0),
    Passed           bit           NULL,
    TabSwitches      int           NOT NULL CONSTRAINT DF_Attempt_TabSwitches DEFAULT (0),
    Pastes           int           NOT NULL CONSTRAINT DF_Attempt_Pastes DEFAULT (0),
    RowVersion       rowversion    NOT NULL,
    CONSTRAINT UQ_Attempt_Invitation_No UNIQUE (InvitationId, AttemptNo)
);

/* An attempt freezes its sections, questions, options and accepted answers at start, so later edits to the
   bank or the test never change an attempt already started. Translations are frozen with them. */
CREATE TABLE assessment.AttemptSection (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttemptSection PRIMARY KEY,
    AttemptId    int            NOT NULL CONSTRAINT FK_AttemptSection_Attempt REFERENCES assessment.Attempt (Id) ON DELETE CASCADE,
    SectionNo    int            NOT NULL,
    Title        nvarchar(140)  NOT NULL,
    Instructions nvarchar(2000) NOT NULL,
    Minutes      int            NOT NULL,
    PassMark     decimal(5,2)   NOT NULL,
    CONSTRAINT UQ_AttemptSection_Attempt_No UNIQUE (AttemptId, SectionNo)
);

CREATE TABLE assessment.AttemptSectionTranslation (
    AttemptSectionId int            NOT NULL CONSTRAINT FK_AttemptSectionTranslation_Section REFERENCES assessment.AttemptSection (Id) ON DELETE CASCADE,
    LanguageCode     varchar(10)    NOT NULL,
    Title            nvarchar(140)  NOT NULL,
    Instructions     nvarchar(2000) NOT NULL,
    CONSTRAINT PK_AttemptSectionTranslation PRIMARY KEY (AttemptSectionId, LanguageCode)
);

CREATE TABLE assessment.AttemptQuestion (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttemptQuestion PRIMARY KEY,
    AttemptSectionId int            NOT NULL CONSTRAINT FK_AttemptQuestion_Section REFERENCES assessment.AttemptSection (Id) ON DELETE CASCADE,
    QuestionKey      varchar(10)    NOT NULL,
    SortOrder        int            NOT NULL,
    QuestionId       int            NOT NULL CONSTRAINT FK_AttemptQuestion_Question REFERENCES assessment.Question (Id),
    QuestionType     varchar(20)    NOT NULL,
    [Text]           nvarchar(max)  NOT NULL,
    Points           decimal(6,2)   NOT NULL,
    Tolerance        decimal(18,4)  NOT NULL,
    Rubric           nvarchar(max)  NOT NULL,
    MaxWords         int            NOT NULL,
    -- the candidate's answer and its marking
    AnswerText       nvarchar(max)  NULL,
    AnswerFileId     uniqueidentifier NULL CONSTRAINT FK_AttemptQuestion_AnswerFile REFERENCES ops.StoredFile (Id),
    AnswerFileName   nvarchar(200)  NULL,
    AnsweredAt       datetime2(0)   NULL,
    AutoPoints       decimal(6,2)   NOT NULL CONSTRAINT DF_AttemptQuestion_AutoPoints DEFAULT (0),
    NeedsMarking     bit            NOT NULL CONSTRAINT DF_AttemptQuestion_NeedsMarking DEFAULT (0),
    MarkerPoints     decimal(6,2)   NULL,
    MarkerComment    nvarchar(1000) NOT NULL CONSTRAINT DF_AttemptQuestion_MarkerComment DEFAULT (N''),
    MarkedByStaffId  int            NULL CONSTRAINT FK_AttemptQuestion_MarkedBy REFERENCES [identity].StaffUser (Id),
    MarkedAt         datetime2(0)   NULL,
    SuggestedPoints  decimal(6,2)   NULL,
    SuggestionReason nvarchar(600)  NOT NULL CONSTRAINT DF_AttemptQuestion_SuggestionReason DEFAULT (N''),
    CONSTRAINT CK_AttemptQuestion_MarkerPoints CHECK (MarkerPoints IS NULL OR (MarkerPoints >= 0 AND MarkerPoints <= Points))
);
CREATE INDEX IX_AttemptQuestion_Section ON assessment.AttemptQuestion (AttemptSectionId, SortOrder);

CREATE TABLE assessment.AttemptQuestionTranslation (
    AttemptQuestionId int           NOT NULL CONSTRAINT FK_AttemptQuestionTranslation_Question REFERENCES assessment.AttemptQuestion (Id) ON DELETE CASCADE,
    LanguageCode      varchar(10)   NOT NULL,
    [Text]            nvarchar(max) NOT NULL,
    CONSTRAINT PK_AttemptQuestionTranslation PRIMARY KEY (AttemptQuestionId, LanguageCode)
);

CREATE TABLE assessment.AttemptOption (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttemptOption PRIMARY KEY,
    AttemptQuestionId int           NOT NULL CONSTRAINT FK_AttemptOption_Question REFERENCES assessment.AttemptQuestion (Id) ON DELETE CASCADE,
    OptionKey         char(1)       NOT NULL,
    SortOrder         int           NOT NULL,
    [Text]            nvarchar(500) NOT NULL,
    IsCorrect         bit           NOT NULL,
    IsSelected        bit           NOT NULL CONSTRAINT DF_AttemptOption_IsSelected DEFAULT (0),
    CONSTRAINT UQ_AttemptOption_Question_Key UNIQUE (AttemptQuestionId, OptionKey)
);

CREATE TABLE assessment.AttemptOptionTranslation (
    AttemptOptionId int           NOT NULL CONSTRAINT FK_AttemptOptionTranslation_Option REFERENCES assessment.AttemptOption (Id) ON DELETE CASCADE,
    LanguageCode    varchar(10)   NOT NULL,
    [Text]          nvarchar(500) NOT NULL,
    CONSTRAINT PK_AttemptOptionTranslation PRIMARY KEY (AttemptOptionId, LanguageCode)
);

CREATE TABLE assessment.AttemptAcceptedAnswer (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttemptAcceptedAnswer PRIMARY KEY,
    AttemptQuestionId int           NOT NULL CONSTRAINT FK_AttemptAcceptedAnswer_Question REFERENCES assessment.AttemptQuestion (Id) ON DELETE CASCADE,
    Answer            nvarchar(500) NOT NULL
);

/* ===================== ops ===================== */
CREATE TABLE ops.EmailMessage (
    Id            bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmailMessage PRIMARY KEY,
    ToAddress     nvarchar(254)  NOT NULL,
    Subject       nvarchar(300)  NOT NULL,
    HtmlBody      nvarchar(max)  NOT NULL,
    Status        varchar(10)    NOT NULL CONSTRAINT CK_EmailMessage_Status CHECK (Status IN ('Queued', 'Sent', 'Failed')),
    Attempts      int            NOT NULL CONSTRAINT DF_EmailMessage_Attempts DEFAULT (0),
    NextAttemptAt datetime2(0)   NOT NULL,
    LastError     nvarchar(500)  NOT NULL CONSTRAINT DF_EmailMessage_LastError DEFAULT (N''),
    CreatedAt     datetime2(0)   NOT NULL,
    SentAt        datetime2(0)   NULL
);
CREATE INDEX IX_EmailMessage_Queue ON ops.EmailMessage (Status, NextAttemptAt);

CREATE TABLE ops.BackgroundJob (
    Id          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BackgroundJob PRIMARY KEY,
    JobType     varchar(60)    NOT NULL,
    TargetId    int            NOT NULL,
    Status      varchar(10)    NOT NULL CONSTRAINT CK_BackgroundJob_Status CHECK (Status IN ('Queued', 'Running', 'Done', 'Failed')),
    Attempts    int            NOT NULL CONSTRAINT DF_BackgroundJob_Attempts DEFAULT (0),
    RunAfter    datetime2(0)   NOT NULL,
    LockedUntil datetime2(0)   NULL,
    LastError   nvarchar(1000) NOT NULL CONSTRAINT DF_BackgroundJob_LastError DEFAULT (N''),
    CreatedAt   datetime2(0)   NOT NULL,
    CompletedAt datetime2(0)   NULL
);
CREATE INDEX IX_BackgroundJob_Queue ON ops.BackgroundJob (Status, RunAfter);
CREATE UNIQUE INDEX UQ_BackgroundJob_Pending ON ops.BackgroundJob (JobType, TargetId) WHERE Status IN ('Queued', 'Running');

CREATE TABLE ops.AuditEntry (
    Id          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditEntry PRIMARY KEY,
    EntityName  varchar(100)  NOT NULL,
    EntityKey   varchar(60)   NOT NULL,
    Action      varchar(10)   NOT NULL CONSTRAINT CK_AuditEntry_Action CHECK (Action IN ('Created', 'Updated', 'Deleted')),
    Changes     nvarchar(max) NOT NULL CONSTRAINT CK_AuditEntry_Changes CHECK (ISJSON(Changes) = 1),
    ActorType   varchar(10)   NOT NULL CONSTRAINT CK_AuditEntry_ActorType CHECK (ActorType IN ('Staff', 'Applicant', 'System')),
    ActorId     int           NULL,
    ActorName   nvarchar(140) NOT NULL,
    OccurredAt  datetime2(0)  NOT NULL
);
CREATE INDEX IX_AuditEntry_Entity ON ops.AuditEntry (EntityName, EntityKey, OccurredAt);

CREATE TABLE ops.AssistantMessage (
    Id               bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AssistantMessage PRIMARY KEY,
    StaffUserId      int            NOT NULL CONSTRAINT FK_AssistantMessage_Staff REFERENCES [identity].StaffUser (Id),
    SessionId        varchar(40)    NOT NULL,
    Question         nvarchar(4000) NOT NULL,
    Reply            nvarchar(max)  NOT NULL,
    ToolTrace        nvarchar(max)  NOT NULL CONSTRAINT CK_AssistantMessage_ToolTrace CHECK (ISJSON(ToolTrace) = 1),
    Status           varchar(12)    NOT NULL CONSTRAINT CK_AssistantMessage_Status CHECK (Status IN ('Success', 'Error', 'RateLimited')),
    Model            varchar(100)   NOT NULL,
    PromptTokens     int            NOT NULL,
    CompletionTokens int            NOT NULL,
    LatencyMs        int            NOT NULL,
    CreatedAt        datetime2(0)   NOT NULL
);
CREATE INDEX IX_AssistantMessage_Session ON ops.AssistantMessage (StaffUserId, SessionId, CreatedAt);

CREATE TABLE ops.AssistantAction (
    Id            uniqueidentifier NOT NULL CONSTRAINT PK_AssistantAction PRIMARY KEY,
    StaffUserId   int            NOT NULL CONSTRAINT FK_AssistantAction_Staff REFERENCES [identity].StaffUser (Id),
    SessionId     varchar(40)    NOT NULL,
    ActionName    varchar(60)    NOT NULL,
    Arguments     nvarchar(4000) NOT NULL CONSTRAINT CK_AssistantAction_Arguments CHECK (ISJSON(Arguments) = 1),
    Label         nvarchar(600)  NOT NULL,
    ExpiresAt     datetime2(0)   NOT NULL,
    UsedAt        datetime2(0)   NULL,
    Result        nvarchar(600)  NOT NULL CONSTRAINT DF_AssistantAction_Result DEFAULT (N'')
);

/* Readable codes (JOB-2026-0001 and so on) come from sequences, so they never repeat. */
CREATE SEQUENCE hiring.JobOpeningCode AS int START WITH 1;
CREATE SEQUENCE hiring.JobApplicationCode AS int START WITH 1;
CREATE SEQUENCE hiring.InterviewCode AS int START WITH 1;
CREATE SEQUENCE hiring.JobOfferCode AS int START WITH 1;
CREATE SEQUENCE assessment.InvitationCode AS int START WITH 1;

CREATE TABLE ops.SchemaVersion (
    ScriptName varchar(200) NOT NULL CONSTRAINT PK_SchemaVersion PRIMARY KEY,
    AppliedAt  datetime2(0) NOT NULL
);

/* ===================== reference data ===================== */
INSERT INTO core.Language (Code, NativeName, EnglishName, IsRtl, IsEnabled, SortOrder) VALUES
    ('ar', N'العربية', N'Arabic', 1, 1, 1),
    ('en', N'English', N'English', 0, 1, 2);

INSERT INTO core.PortalSetting (Id, DefaultLanguageCode, UpdatedAt) VALUES (1, 'ar', SYSUTCDATETIME());
