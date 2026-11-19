/* Nationality on applications (optional, from the apply form): Emiratisation and fairness reporting only. */
ALTER TABLE hiring.JobApplication ADD Nationality nvarchar(140) NOT NULL CONSTRAINT DF_JobApplication_Nationality DEFAULT (N'');
GO
