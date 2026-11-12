/* Each language of a job has its own location text. English versions of jobs in an emirate start with its English name. */
ALTER TABLE hiring.JobOpeningTranslation ADD Location nvarchar(140) NOT NULL CONSTRAINT DF_JobOpeningTranslation_Location DEFAULT (N'');
GO
UPDATE t SET Location = v.En
FROM hiring.JobOpeningTranslation t
JOIN hiring.JobOpening j ON j.Id = t.JobOpeningId
JOIN (VALUES (N'أبوظبي', N'Abu Dhabi'), (N'ابوظبي', N'Abu Dhabi'), (N'أبو ظبي', N'Abu Dhabi'), (N'دبي', N'Dubai'), (N'الشارقة', N'Sharjah'), (N'عجمان', N'Ajman'),
  (N'أم القيوين', N'Umm Al Quwain'), (N'رأس الخيمة', N'Ras Al Khaimah'), (N'الفجيرة', N'Fujairah')) AS v (Ar, En) ON v.Ar = LTRIM(RTRIM(j.Location))
WHERE t.LanguageCode = 'en' AND t.Location = N'';
GO
