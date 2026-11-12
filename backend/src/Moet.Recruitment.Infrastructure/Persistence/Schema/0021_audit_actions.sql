/* The audit log covers actions too (sign-ins, sending, downloads, views of personal data), says whether the assistant was
   involved and what was approved, groups entries of one request, and takes reviewers' comments. */
ALTER TABLE ops.AuditEntry DROP CONSTRAINT CK_AuditEntry_Action;
GO
ALTER TABLE ops.AuditEntry ADD CONSTRAINT CK_AuditEntry_Action CHECK (Action IN ('Created', 'Updated', 'Deleted', 'Action'));
GO
ALTER TABLE ops.AuditEntry ADD
  Description nvarchar(400) NOT NULL CONSTRAINT DF_AuditEntry_Description DEFAULT (N''),
  Via varchar(20) NOT NULL CONSTRAINT DF_AuditEntry_Via DEFAULT (''),
  Comment nvarchar(1000) NOT NULL CONSTRAINT DF_AuditEntry_Comment DEFAULT (N''),
  ProposalId uniqueidentifier NULL,
  RequestId varchar(40) NOT NULL CONSTRAINT DF_AuditEntry_RequestId DEFAULT (''),
  IpAddress varchar(45) NOT NULL CONSTRAINT DF_AuditEntry_IpAddress DEFAULT ('');
GO
CREATE INDEX IX_AuditEntry_When ON ops.AuditEntry (OccurredAt DESC);
GO
CREATE INDEX IX_AuditEntry_Actor ON ops.AuditEntry (ActorType, ActorId, OccurredAt DESC);
GO
CREATE TABLE ops.AuditComment (
  Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditComment PRIMARY KEY,
  AuditEntryId bigint NOT NULL CONSTRAINT FK_AuditComment_Entry REFERENCES ops.AuditEntry (Id) ON DELETE CASCADE,
  StaffUserId int NOT NULL,
  StaffName nvarchar(140) NOT NULL,
  Text nvarchar(2000) NOT NULL,
  CreatedAt datetime2(3) NOT NULL
);
GO
