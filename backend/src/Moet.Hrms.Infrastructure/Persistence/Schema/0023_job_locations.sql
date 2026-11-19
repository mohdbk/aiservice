/* Locations are a list HR keeps (any place, with a name in each language), and jobs link to one. The seven emirates start the
   list; every location typed on existing jobs is added and linked. */
CREATE TABLE core.JobLocation (
  Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_JobLocation PRIMARY KEY,
  Name nvarchar(140) NOT NULL,
  SourceLanguageCode varchar(10) NOT NULL CONSTRAINT DF_JobLocation_Source DEFAULT ('ar'),
  IsActive bit NOT NULL CONSTRAINT DF_JobLocation_Active DEFAULT (1),
  SortOrder int NOT NULL CONSTRAINT DF_JobLocation_Sort DEFAULT (0)
);
GO
CREATE TABLE core.JobLocationTranslation (
  JobLocationId int NOT NULL CONSTRAINT FK_JobLocationTranslation_Location REFERENCES core.JobLocation (Id) ON DELETE CASCADE,
  LanguageCode varchar(10) NOT NULL,
  Name nvarchar(140) NOT NULL,
  CONSTRAINT PK_JobLocationTranslation PRIMARY KEY (JobLocationId, LanguageCode)
);
GO
INSERT INTO core.JobLocation (Name, SortOrder) VALUES
 (N'أبوظبي', 1), (N'دبي', 2), (N'الشارقة', 3), (N'عجمان', 4), (N'أم القيوين', 5), (N'رأس الخيمة', 6), (N'الفجيرة', 7);
GO
INSERT INTO core.JobLocationTranslation (JobLocationId, LanguageCode, Name)
SELECT l.Id, 'en', v.En FROM core.JobLocation l
JOIN (VALUES (N'أبوظبي', N'Abu Dhabi'), (N'دبي', N'Dubai'), (N'الشارقة', N'Sharjah'), (N'عجمان', N'Ajman'),
  (N'أم القيوين', N'Umm Al Quwain'), (N'رأس الخيمة', N'Ras Al Khaimah'), (N'الفجيرة', N'Fujairah')) AS v (Ar, En) ON v.Ar = l.Name;
GO
ALTER TABLE hiring.JobOpening ADD LocationId int NULL CONSTRAINT FK_JobOpening_Location REFERENCES core.JobLocation (Id);
GO
/* Typed locations that are not on the list yet (other spellings of an emirate are matched first). */
UPDATE hiring.JobOpening SET Location = CASE LTRIM(RTRIM(Location))
  WHEN N'ابوظبي' THEN N'أبوظبي' WHEN N'أبو ظبي' THEN N'أبوظبي' WHEN N'ابو ظبي' THEN N'أبوظبي' WHEN N'Abu Dhabi' THEN N'أبوظبي'
  WHEN N'Dubai' THEN N'دبي' WHEN N'Sharjah' THEN N'الشارقة' WHEN N'Ajman' THEN N'عجمان' WHEN N'ام القيوين' THEN N'أم القيوين'
  WHEN N'Umm Al Quwain' THEN N'أم القيوين' WHEN N'راس الخيمة' THEN N'رأس الخيمة' WHEN N'Ras Al Khaimah' THEN N'رأس الخيمة' WHEN N'Fujairah' THEN N'الفجيرة'
  ELSE LTRIM(RTRIM(Location)) END;
GO
INSERT INTO core.JobLocation (Name, SortOrder)
SELECT DISTINCT j.Location, 100 FROM hiring.JobOpening j
WHERE j.Location <> N'' AND NOT EXISTS (SELECT 1 FROM core.JobLocation l WHERE l.Name = j.Location);
GO
/* English names typed on the English version of those jobs become the location's English name. */
INSERT INTO core.JobLocationTranslation (JobLocationId, LanguageCode, Name)
SELECT l.Id, 'en', MIN(t.Location) FROM core.JobLocation l
JOIN hiring.JobOpening j ON j.Location = l.Name
JOIN hiring.JobOpeningTranslation t ON t.JobOpeningId = j.Id AND t.LanguageCode = 'en' AND t.Location <> N''
WHERE NOT EXISTS (SELECT 1 FROM core.JobLocationTranslation x WHERE x.JobLocationId = l.Id AND x.LanguageCode = 'en')
GROUP BY l.Id;
GO
UPDATE j SET LocationId = l.Id FROM hiring.JobOpening j JOIN core.JobLocation l ON l.Name = j.Location;
GO
