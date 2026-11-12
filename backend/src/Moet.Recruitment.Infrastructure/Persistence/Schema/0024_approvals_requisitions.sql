/* Approvals (publishing a job, sending an offer, opening a requisition) and requisitions for new posts. Approvals for jobs
   and offers are off until switched on in the portal settings. */
ALTER TABLE core.PortalSetting ADD
  RequireJobApproval bit NOT NULL CONSTRAINT DF_PortalSetting_JobApproval DEFAULT (0),
  RequireOfferApproval bit NOT NULL CONSTRAINT DF_PortalSetting_OfferApproval DEFAULT (0);
GO
CREATE TABLE hiring.ApprovalRequest (
  Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ApprovalRequest PRIMARY KEY,
  Kind varchar(20) NOT NULL CONSTRAINT CK_ApprovalRequest_Kind CHECK (Kind IN ('JobPublish', 'OfferSend', 'Requisition')),
  RecordId int NOT NULL,
  Title nvarchar(300) NOT NULL,
  Note nvarchar(2000) NOT NULL,
  Status varchar(10) NOT NULL CONSTRAINT CK_ApprovalRequest_Status CHECK (Status IN ('Pending', 'Approved', 'Rejected', 'Cancelled')),
  RequestedByStaffId int NOT NULL,
  RequestedByName nvarchar(140) NOT NULL,
  RequestedAt datetime2(3) NOT NULL,
  DecidedByStaffId int NULL,
  DecidedByName nvarchar(140) NOT NULL,
  DecidedAt datetime2(3) NULL,
  Comment nvarchar(2000) NOT NULL
);
GO
CREATE INDEX IX_ApprovalRequest_Status ON hiring.ApprovalRequest (Status, RequestedAt);
GO
CREATE TABLE hiring.Requisition (
  Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Requisition PRIMARY KEY,
  Code varchar(20) NOT NULL CONSTRAINT UQ_Requisition_Code UNIQUE,
  Title nvarchar(140) NOT NULL,
  TitleEn nvarchar(140) NOT NULL,
  DepartmentId int NULL CONSTRAINT FK_Requisition_Department REFERENCES core.Department (Id),
  LocationId int NULL CONSTRAINT FK_Requisition_Location REFERENCES core.JobLocation (Id),
  EmploymentType varchar(20) NOT NULL,
  Grade nvarchar(50) NOT NULL,
  Headcount int NOT NULL,
  BudgetMonthly decimal(18,4) NULL,
  Justification nvarchar(4000) NOT NULL,
  NeededBy date NULL,
  Status varchar(10) NOT NULL CONSTRAINT CK_Requisition_Status CHECK (Status IN ('Pending', 'Approved', 'Rejected', 'Filled', 'Cancelled')),
  JobOpeningId int NULL CONSTRAINT FK_Requisition_Job REFERENCES hiring.JobOpening (Id),
  RequestedByStaffId int NOT NULL,
  RequestedByName nvarchar(140) NOT NULL,
  CreatedAt datetime2(3) NOT NULL,
  UpdatedAt datetime2(3) NOT NULL
);
GO
CREATE SEQUENCE hiring.RequisitionCode AS int START WITH 1;
GO
