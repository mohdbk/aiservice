/* The Ministry's workplace questionnaires (Big Five scored 5 to 25, and the 16-type questionnaire) and HR's role profiles. */

ALTER TABLE assessment.Assessment DROP CONSTRAINT CK_Assessment_PersonalityModel;
ALTER TABLE assessment.Assessment DROP CONSTRAINT DF_Assessment_PersonalityModel;
ALTER TABLE assessment.Assessment ALTER COLUMN PersonalityModel varchar(20) NOT NULL;
ALTER TABLE assessment.Assessment ADD
    CONSTRAINT DF_Assessment_PersonalityModel DEFAULT ('None') FOR PersonalityModel,
    CONSTRAINT CK_Assessment_PersonalityModel CHECK (PersonalityModel IN ('None', 'BigFive', 'SixteenPF', 'WorkplaceBigFive', 'WorkplaceType16'));
GO

/* Role profiles: weights and target bands per dimension (Big Five), or suitable types (16-type questionnaire). */
CREATE TABLE assessment.RoleProfile (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RoleProfile PRIMARY KEY,
    NameAr           nvarchar(150)  NOT NULL,
    NameEn           nvarchar(150)  NOT NULL,
    PersonalityModel varchar(20)    NOT NULL CONSTRAINT CK_RoleProfile_Model CHECK (PersonalityModel IN ('BigFive', 'WorkplaceBigFive', 'WorkplaceType16')),
    Weights          nvarchar(1000) NOT NULL CONSTRAINT DF_RoleProfile_Weights DEFAULT (N'{}') CONSTRAINT CK_RoleProfile_Weights CHECK (ISJSON(Weights) = 1),
    Targets          nvarchar(1000) NOT NULL CONSTRAINT DF_RoleProfile_Targets DEFAULT (N'{}') CONSTRAINT CK_RoleProfile_Targets CHECK (ISJSON(Targets) = 1),
    Types            varchar(100)   NOT NULL CONSTRAINT DF_RoleProfile_Types DEFAULT (''),
    StrongFrom       tinyint        NOT NULL CONSTRAINT DF_RoleProfile_StrongFrom DEFAULT (75),
    ModerateFrom     tinyint        NOT NULL CONSTRAINT DF_RoleProfile_ModerateFrom DEFAULT (55),
    IsActive         bit            NOT NULL CONSTRAINT DF_RoleProfile_IsActive DEFAULT (1),
    SortOrder        int            NOT NULL CONSTRAINT DF_RoleProfile_SortOrder DEFAULT (0),
    CreatedAt        datetime2(0)   NOT NULL,
    UpdatedAt        datetime2(0)   NOT NULL,
    UpdatedByStaffId int            NULL CONSTRAINT FK_RoleProfile_Staff REFERENCES [identity].StaffUser (Id),
    RowVersion       rowversion     NOT NULL,
    CONSTRAINT CK_RoleProfile_Thresholds CHECK (ModerateFrom < StrongFrom AND StrongFrom <= 100)
);
GO

/* HR's starting profiles from the workplace personality proposal; HR maps every other position in the backoffice. */
INSERT INTO assessment.RoleProfile (NameAr, NameEn, PersonalityModel, Weights, Targets, Types, SortOrder, CreatedAt, UpdatedAt) VALUES
 (N'مدير مشروع', N'Project Manager', 'WorkplaceBigFive', N'{"C":35,"ES":25,"E":15,"A":15,"O":10}', N'{"C":"High","ES":"High","A":"Moderate","E":"Moderate","O":"Moderate"}', '', 1, SYSUTCDATETIME(), SYSUTCDATETIME()),
 (N'تنفيذي مبيعات', N'Sales Executive', 'WorkplaceBigFive', N'{"E":35,"ES":25,"A":20,"C":10,"O":10}', N'{}', '', 2, SYSUTCDATETIME(), SYSUTCDATETIME()),
 (N'محلل', N'Analyst', 'WorkplaceBigFive', N'{"C":40,"O":25,"ES":20,"A":10,"E":5}', N'{}', '', 3, SYSUTCDATETIME(), SYSUTCDATETIME()),
 (N'أدوار قيادية', N'Leadership Roles', 'WorkplaceType16', N'{}', N'{}', 'ENTJ,ENFJ,ESTJ,INTJ', 4, SYSUTCDATETIME(), SYSUTCDATETIME()),
 (N'أدوار إدارة المشاريع', N'Project Management Roles', 'WorkplaceType16', N'{}', N'{}', 'ENTJ,ESTJ,INTJ,ISTJ', 5, SYSUTCDATETIME(), SYSUTCDATETIME()),
 (N'أدوار التعامل مع الناس', N'People-Oriented Roles', 'WorkplaceType16', N'{}', N'{}', 'ENFJ,ESFJ,ENFP,ESFP', 6, SYSUTCDATETIME(), SYSUTCDATETIME()),
 (N'أدوار تحليلية', N'Analytical Roles', 'WorkplaceType16', N'{}', N'{}', 'INTJ,INTP,ISTJ,ISTP', 7, SYSUTCDATETIME(), SYSUTCDATETIME()),
 (N'أدوار الابتكار', N'Innovation Roles', 'WorkplaceType16', N'{}', N'{}', 'ENTP,INTP,ENFP,INTJ', 8, SYSUTCDATETIME(), SYSUTCDATETIME());
GO
