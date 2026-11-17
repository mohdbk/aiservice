/* Staff notification emails: a morning digest of what needs them and a weekly hiring summary, each switched on per
   person from the notifications bell. The dates record the last one sent, so each goes once per day or week. */
ALTER TABLE [identity].StaffUser ADD
  DailyDigest bit NOT NULL CONSTRAINT DF_StaffUser_DailyDigest DEFAULT (0),
  WeeklyReport bit NOT NULL CONSTRAINT DF_StaffUser_WeeklyReport DEFAULT (0),
  DigestSentOn date NULL,
  WeeklySentOn date NULL;
GO
/* An interview guide prepared for the panel (questions per skill, and what a good answer covers), as JSON. */
ALTER TABLE hiring.Interview ADD Guide nvarchar(max) NOT NULL CONSTRAINT DF_Interview_Guide DEFAULT ('');
GO
