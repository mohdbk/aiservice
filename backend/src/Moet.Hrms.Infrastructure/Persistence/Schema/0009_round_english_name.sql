/* Interview rounds get an English name, shown when the backoffice is in English. */
ALTER TABLE hiring.InterviewRound ADD NameEn nvarchar(140) NOT NULL CONSTRAINT DF_InterviewRound_NameEn DEFAULT (N'');
GO
