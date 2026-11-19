/* Personality questionnaires (Big Five, 16PF): agreement-scale statements keyed to traits, and trait profiles. */

ALTER TABLE assessment.Question DROP CONSTRAINT CK_Question_Type;
ALTER TABLE assessment.Question ADD CONSTRAINT CK_Question_Type CHECK (QuestionType IN ('SingleChoice', 'MultipleChoice', 'TrueFalse', 'ShortAnswer', 'Number', 'Essay', 'FileUpload', 'Likert'));
ALTER TABLE assessment.Question ADD
    Trait        varchar(10) NOT NULL CONSTRAINT DF_Question_Trait DEFAULT (''),
    ReverseKeyed bit         NOT NULL CONSTRAINT DF_Question_ReverseKeyed DEFAULT (0);
GO

ALTER TABLE assessment.Assessment DROP CONSTRAINT CK_Assessment_Type;
ALTER TABLE assessment.Assessment ADD CONSTRAINT CK_Assessment_Type CHECK (AssessmentType IN ('KnowledgeTest', 'LanguageCheck', 'SituationalJudgement', 'WorkSample', 'Aptitude', 'Other', 'Personality'));
ALTER TABLE assessment.Assessment ADD
    PersonalityModel varchar(12)    NOT NULL CONSTRAINT DF_Assessment_PersonalityModel DEFAULT ('None')
                                    CONSTRAINT CK_Assessment_PersonalityModel CHECK (PersonalityModel IN ('None', 'BigFive', 'SixteenPF')),
    TraitNorms       nvarchar(4000) NOT NULL CONSTRAINT DF_Assessment_TraitNorms DEFAULT (N'{}')
                                    CONSTRAINT CK_Assessment_TraitNorms CHECK (ISJSON(TraitNorms) = 1);
GO

ALTER TABLE assessment.Invitation DROP CONSTRAINT CK_Invitation_Result;
ALTER TABLE assessment.Invitation ADD CONSTRAINT CK_Invitation_Result CHECK (Result IN ('Pass', 'Fail', 'AwaitingMarking', 'Completed'));

ALTER TABLE assessment.Attempt ADD TraitProfile nvarchar(max) NULL CONSTRAINT CK_Attempt_TraitProfile CHECK (TraitProfile IS NULL OR ISJSON(TraitProfile) = 1);

ALTER TABLE assessment.AttemptQuestion ADD
    Trait        varchar(10) NOT NULL CONSTRAINT DF_AttemptQuestion_Trait DEFAULT (''),
    ReverseKeyed bit         NOT NULL CONSTRAINT DF_AttemptQuestion_ReverseKeyed DEFAULT (0);
GO
