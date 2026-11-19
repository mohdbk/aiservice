/* Health alert emails to system administrators (on by default; they go out only when email is set up). */
CREATE TABLE ops.HealthAlertSetting (
  Id tinyint NOT NULL CONSTRAINT PK_HealthAlertSetting PRIMARY KEY,
  IsEnabled bit NOT NULL,
  ExtraRecipients nvarchar(2000) NOT NULL,
  LastProblems nvarchar(1000) NOT NULL,
  LastAlertAt datetime2(3) NULL,
  UpdatedAt datetime2(3) NOT NULL
);
GO
INSERT INTO ops.HealthAlertSetting (Id, IsEnabled, ExtraRecipients, LastProblems, UpdatedAt) VALUES (1, 1, N'', N'', SYSUTCDATETIME());
GO
