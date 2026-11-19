/* Talent pool consent on the account, interview times offered to candidates to pick from, and text messages (SMS/WhatsApp). */
ALTER TABLE [identity].ApplicantAccount ADD
  InTalentPool bit NOT NULL CONSTRAINT DF_ApplicantAccount_TalentPool DEFAULT (0),
  TalentPoolAt datetime2(3) NULL;
GO
CREATE TABLE hiring.InterviewBooking (
  Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewBooking PRIMARY KEY,
  JobApplicationId int NOT NULL CONSTRAINT FK_InterviewBooking_Application REFERENCES hiring.JobApplication (Id),
  InterviewRoundId int NOT NULL CONSTRAINT FK_InterviewBooking_Round REFERENCES hiring.InterviewRound (Id),
  Location nvarchar(300) NOT NULL,
  PanelStaffIds varchar(200) NOT NULL,
  Status varchar(10) NOT NULL CONSTRAINT CK_InterviewBooking_Status CHECK (Status IN ('Open', 'Booked', 'Cancelled')),
  InterviewId int NULL CONSTRAINT FK_InterviewBooking_Interview REFERENCES hiring.Interview (Id),
  CreatedAt datetime2(3) NOT NULL,
  CreatedByStaffId int NULL,
  UpdatedAt datetime2(3) NOT NULL
);
GO
CREATE INDEX IX_InterviewBooking_Application ON hiring.InterviewBooking (JobApplicationId, Status);
GO
CREATE TABLE hiring.InterviewBookingSlot (
  Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewBookingSlot PRIMARY KEY,
  InterviewBookingId int NOT NULL CONSTRAINT FK_InterviewBookingSlot_Booking REFERENCES hiring.InterviewBooking (Id) ON DELETE CASCADE,
  StartsAt datetime2(0) NOT NULL,
  EndsAt datetime2(0) NOT NULL
);
GO
CREATE TABLE ops.TextSetting (
  Id tinyint NOT NULL CONSTRAINT PK_TextSetting PRIMARY KEY,
  IsEnabled bit NOT NULL,
  Channel varchar(10) NOT NULL CONSTRAINT CK_TextSetting_Channel CHECK (Channel IN ('Sms', 'WhatsApp')),
  Provider varchar(20) NOT NULL,
  Url nvarchar(500) NOT NULL,
  AccountId nvarchar(100) NOT NULL,
  TokenProtected nvarchar(1000) NOT NULL,
  Sender nvarchar(50) NOT NULL,
  Events varchar(500) NOT NULL,
  UpdatedAt datetime2(3) NOT NULL,
  UpdatedByStaffId int NULL
);
GO
INSERT INTO ops.TextSetting (Id, IsEnabled, Channel, Provider, Url, AccountId, TokenProtected, Sender, Events, UpdatedAt)
VALUES (1, 0, 'Sms', 'Webhook', N'', N'', N'', N'', 'interview_scheduled,interview_booking,assessment,offer_sent', SYSUTCDATETIME());
GO
CREATE TABLE ops.TextMessage (
  Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_TextMessage PRIMARY KEY,
  ToNumber varchar(20) NOT NULL,
  Channel varchar(10) NOT NULL,
  Body nvarchar(1000) NOT NULL,
  EventKey varchar(60) NOT NULL,
  Status varchar(10) NOT NULL,
  Attempts int NOT NULL,
  LastError nvarchar(1000) NOT NULL,
  NextAttemptAt datetime2(3) NOT NULL,
  CreatedAt datetime2(3) NOT NULL,
  SentAt datetime2(3) NULL
);
GO
CREATE INDEX IX_TextMessage_Due ON ops.TextMessage (Status, NextAttemptAt);
GO
/* Emails for the new events: an invitation from the talent pool and interview times to choose from. */
INSERT INTO ops.EmailTemplate ([Key], [Event], NameAr, NameEn, SubjectAr, BodyAr, SubjectEn, BodyEn, BodyHtmlAr, BodyHtmlEn, IsEnabled, CreatedAt, UpdatedAt)
VALUES
('talent_invite', 'talent_invite', N'دعوة للتقديم', N'Invitation to Apply',
 N'وظيفة قد تناسبك: {job_title}', N'مرحبا {name}،' + CHAR(10) + CHAR(10) + N'نعتقد أن وظيفة {job_title} قد تناسبك، وندعوك للاطلاع عليها والتقديم إن رغبت.' + CHAR(10) + CHAR(10) + N'{link}',
 N'A job that may suit you: {job_title}', N'Dear {name},' + CHAR(10) + CHAR(10) + N'We think the {job_title} role may suit you and invite you to look at it and apply if you wish.' + CHAR(10) + CHAR(10) + N'{link}',
 N'', N'', 1, SYSUTCDATETIME(), SYSUTCDATETIME()),
('interview_booking', 'interview_booking', N'اختيار موعد المقابلة', N'Choose an Interview Time',
 N'اختر موعد مقابلتك: {job_title}', N'مرحبا {name}،' + CHAR(10) + CHAR(10) + N'يسعدنا دعوتك إلى {round} لوظيفة {job_title}. يرجى اختيار الموعد المناسب لك.' + CHAR(10) + CHAR(10) + N'{link}',
 N'Choose your interview time: {job_title}', N'Dear {name},' + CHAR(10) + CHAR(10) + N'We are pleased to invite you to the {round} for the {job_title} role. Please choose the time that suits you.' + CHAR(10) + CHAR(10) + N'{link}',
 N'', N'', 1, SYSUTCDATETIME(), SYSUTCDATETIME());
GO
