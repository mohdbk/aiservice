/* Job alerts: candidates' saved searches, and which jobs each one has already sent. */
CREATE TABLE hiring.JobAlert (
  Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_JobAlert PRIMARY KEY,
  ApplicantAccountId int NOT NULL CONSTRAINT FK_JobAlert_Account REFERENCES [identity].ApplicantAccount (Id) ON DELETE CASCADE,
  Keywords nvarchar(300) NOT NULL,
  DepartmentId int NULL CONSTRAINT FK_JobAlert_Department REFERENCES core.Department (Id),
  LocationId int NULL CONSTRAINT FK_JobAlert_Location REFERENCES core.JobLocation (Id),
  LanguageCode varchar(10) NOT NULL,
  CreatedAt datetime2(3) NOT NULL,
  LastSentAt datetime2(3) NULL
);
GO
CREATE INDEX IX_JobAlert_Account ON hiring.JobAlert (ApplicantAccountId);
GO
CREATE TABLE hiring.JobAlertSent (
  JobAlertId int NOT NULL CONSTRAINT FK_JobAlertSent_Alert REFERENCES hiring.JobAlert (Id) ON DELETE CASCADE,
  JobOpeningId int NOT NULL CONSTRAINT FK_JobAlertSent_Job REFERENCES hiring.JobOpening (Id) ON DELETE CASCADE,
  SentAt datetime2(3) NOT NULL,
  CONSTRAINT PK_JobAlertSent PRIMARY KEY (JobAlertId, JobOpeningId)
);
GO
INSERT INTO ops.EmailTemplate ([Key], [Event], NameAr, NameEn, SubjectAr, BodyAr, SubjectEn, BodyEn, BodyHtmlAr, BodyHtmlEn, IsEnabled, CreatedAt, UpdatedAt)
VALUES
('job_alert', 'job_alert', N'تنبيه وظائف', N'Job Alert',
 N'وظائف جديدة تطابق بحثك', N'مرحبا {name}،' + CHAR(10) + CHAR(10) + N'نُشرت وظائف جديدة تطابق تنبيهك:' + CHAR(10) + CHAR(10) + N'{jobs}' + CHAR(10) + CHAR(10) + N'{link}',
 N'New jobs that match your search', N'Dear {name},' + CHAR(10) + CHAR(10) + N'New jobs that match your alert have been published:' + CHAR(10) + CHAR(10) + N'{jobs}' + CHAR(10) + CHAR(10) + N'{link}',
 N'', N'', 1, SYSUTCDATETIME(), SYSUTCDATETIME());
GO
