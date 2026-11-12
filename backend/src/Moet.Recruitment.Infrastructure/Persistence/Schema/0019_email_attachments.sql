/* Emails can carry a file (the offer letter as PDF), and the email settings say whether offers carry it. */
ALTER TABLE ops.EmailMessage ADD AttachmentFileId uniqueidentifier NULL;
GO
ALTER TABLE ops.EmailSetting ADD AttachOfferPdf bit NOT NULL CONSTRAINT DF_EmailSetting_AttachOfferPdf DEFAULT (1);
GO
