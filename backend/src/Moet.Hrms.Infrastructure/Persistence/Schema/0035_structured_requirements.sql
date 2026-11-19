/* Screening requirements as short structured items: type, minimum years (experience), level (language), whether it is
   of high importance, and a score guide describing what 0, 4, 7 and 10 look like. Essential is the existing IsMustHave. */
ALTER TABLE hiring.JobCriterion ADD
  Category varchar(20)    NOT NULL CONSTRAINT DF_JobCriterion_Category DEFAULT ('Other'),
  MinYears tinyint        NULL,
  [Level]  nvarchar(40)   NOT NULL CONSTRAINT DF_JobCriterion_Level DEFAULT (N''),
  IsHigh   bit            NOT NULL CONSTRAINT DF_JobCriterion_IsHigh DEFAULT (0),
  Guide    nvarchar(2000) NOT NULL CONSTRAINT DF_JobCriterion_Guide DEFAULT (N'');
GO
/* Each requirement's result: Met, Not Met or Unclear for essential ones, the exact quote from the CV and whether it was
   found there, relevant years, what would score higher, and a recruiter's override with the reason. */
ALTER TABLE hiring.CvCriterionScore ADD
  Category          varchar(20)   NOT NULL CONSTRAINT DF_CvCriterionScore_Category DEFAULT ('Other'),
  Status            varchar(10)   NOT NULL CONSTRAINT DF_CvCriterionScore_Status DEFAULT (''),
  Quote             nvarchar(500) NOT NULL CONSTRAINT DF_CvCriterionScore_Quote DEFAULT (N''),
  QuoteFound        bit           NULL,
  RelevantYears     decimal(4,1)  NULL,
  WhyNotHigher      nvarchar(500) NOT NULL CONSTRAINT DF_CvCriterionScore_WhyNotHigher DEFAULT (N''),
  OverrideScore     tinyint       NULL CONSTRAINT CK_CvCriterionScore_Override CHECK (OverrideScore BETWEEN 0 AND 10),
  OverrideReason    nvarchar(500) NOT NULL CONSTRAINT DF_CvCriterionScore_OverrideReason DEFAULT (N''),
  OverrideByStaffId int           NULL,
  OverrideAt        datetime2(0)  NULL;
GO
/* Whether a person should look at the result (an essential requirement unclear, evidence not found in the CV, two
   scorings that disagree, or a poor scan), and why; and how well the CV could be read. */
ALTER TABLE hiring.CvAnalysis ADD
  NeedsReview   bit            NOT NULL CONSTRAINT DF_CvAnalysis_NeedsReview DEFAULT (0),
  ReviewReasons nvarchar(1000) NOT NULL CONSTRAINT DF_CvAnalysis_ReviewReasons DEFAULT (N''),
  ReadQuality   varchar(10)    NOT NULL CONSTRAINT DF_CvAnalysis_ReadQuality DEFAULT ('');
GO
