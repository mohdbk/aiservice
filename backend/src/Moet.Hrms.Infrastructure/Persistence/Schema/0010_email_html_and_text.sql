/* Email templates can have a designed HTML version next to the plain text; every email is sent with both parts. */
ALTER TABLE ops.EmailTemplate ADD
    BodyHtmlAr nvarchar(max) NOT NULL CONSTRAINT DF_EmailTemplate_BodyHtmlAr DEFAULT (N''),
    BodyHtmlEn nvarchar(max) NOT NULL CONSTRAINT DF_EmailTemplate_BodyHtmlEn DEFAULT (N'');
ALTER TABLE ops.EmailMessage ADD TextBody nvarchar(max) NOT NULL CONSTRAINT DF_EmailMessage_TextBody DEFAULT (N'');
GO

/* Offer letter templates can have a designed HTML version too (shown, printed and emailed when present). */
ALTER TABLE hiring.OfferTemplate ADD
    BodyHtmlAr nvarchar(max) NOT NULL CONSTRAINT DF_OfferTemplate_BodyHtmlAr DEFAULT (N''),
    BodyHtmlEn nvarchar(max) NOT NULL CONSTRAINT DF_OfferTemplate_BodyHtmlEn DEFAULT (N'');
GO
