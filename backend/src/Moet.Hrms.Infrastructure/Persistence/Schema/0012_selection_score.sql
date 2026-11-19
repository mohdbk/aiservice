/* Combined selection score: each job's weights (CV, assessments, interviews, personality; 0/40/40/20 by default) and the
   role profile (job family) its personality results are matched against. */
ALTER TABLE hiring.JobOpening ADD
  RoleProfileId int NULL CONSTRAINT FK_JobOpening_RoleProfile REFERENCES assessment.RoleProfile (Id) ON DELETE SET NULL,
  WeightCv tinyint NOT NULL CONSTRAINT DF_JobOpening_WeightCv DEFAULT (0),
  WeightTests tinyint NOT NULL CONSTRAINT DF_JobOpening_WeightTests DEFAULT (40),
  WeightInterview tinyint NOT NULL CONSTRAINT DF_JobOpening_WeightInterview DEFAULT (40),
  WeightPersonality tinyint NOT NULL CONSTRAINT DF_JobOpening_WeightPersonality DEFAULT (20);
GO
ALTER TABLE hiring.JobOpening ADD CONSTRAINT CK_JobOpening_Weights CHECK (WeightCv + WeightTests + WeightInterview + WeightPersonality = 100);
GO
