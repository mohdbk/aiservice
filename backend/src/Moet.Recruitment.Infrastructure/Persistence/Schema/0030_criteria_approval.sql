/* Screening criteria are approved by HR before CVs are scored against them. Jobs whose criteria were already used are
   treated as approved; CVs waiting for criteria have the status Waiting. */
ALTER TABLE hiring.JobOpening ADD CriteriaApprovedAt datetime2(3) NULL, CriteriaApprovedByStaffId int NULL;
GO
UPDATE j SET CriteriaApprovedAt = SYSUTCDATETIME()
FROM hiring.JobOpening j
WHERE EXISTS (SELECT 1 FROM hiring.JobCriterion c WHERE c.JobOpeningId = j.Id);
GO
ALTER TABLE hiring.CvAnalysis DROP CONSTRAINT CK_CvAnalysis_Status;
GO
ALTER TABLE hiring.CvAnalysis ADD CONSTRAINT CK_CvAnalysis_Status CHECK (Status IN ('Pending', 'Processing', 'Scored', 'Failed', 'Skipped', 'Waiting'));
GO
