/* Two-step sign-in for staff (authenticator app codes). Secrets are stored encrypted. Optional per person unless required. */
ALTER TABLE [identity].StaffUser ADD
  TotpSecretProtected nvarchar(500) NOT NULL CONSTRAINT DF_StaffUser_TotpSecret DEFAULT (N''),
  TotpPendingProtected nvarchar(500) NOT NULL CONSTRAINT DF_StaffUser_TotpPending DEFAULT (N''),
  TotpEnabledAt datetime2(3) NULL,
  TotpLastStep bigint NOT NULL CONSTRAINT DF_StaffUser_TotpLastStep DEFAULT (0);
GO
