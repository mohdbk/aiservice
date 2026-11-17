/* Candidates can withdraw an application from the portal. */
ALTER TABLE hiring.JobApplication DROP CONSTRAINT CK_JobApplication_Status;
GO
ALTER TABLE hiring.JobApplication ADD CONSTRAINT CK_JobApplication_Status CHECK (Status IN ('Open', 'Replied', 'Hold', 'Accepted', 'Rejected', 'Withdrawn'));
GO
/* Accepting an offer in the portal takes the candidate's typed full name as their signature, with the time. */
ALTER TABLE hiring.JobOffer ADD SignedName nvarchar(200) NOT NULL CONSTRAINT DF_JobOffer_SignedName DEFAULT (''), SignedAt datetime2 NULL;
GO
