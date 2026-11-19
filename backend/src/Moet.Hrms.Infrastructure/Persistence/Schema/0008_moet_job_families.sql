/* Generic Ministry job families instead of single job titles: one Big Five and one 16-type profile per family. The single-title
   profiles from 0007 are removed when HR has not changed them (an edited one is kept). */
DELETE r FROM assessment.RoleProfile r
JOIN (VALUES
  ('Operations Manager', 'WorkplaceBigFive'),
  ('Business Analyst', 'WorkplaceBigFive'),
  ('HR Officer', 'WorkplaceBigFive'),
  ('Customer Service Officer', 'WorkplaceBigFive'),
  ('Software Developer', 'WorkplaceBigFive'),
  ('Data Scientist', 'WorkplaceBigFive'),
  ('Policy and Economic Advisor', 'WorkplaceBigFive'),
  ('Legal Advisor', 'WorkplaceBigFive'),
  ('Accountant and Financial Analyst', 'WorkplaceBigFive'),
  ('Procurement Specialist', 'WorkplaceBigFive'),
  ('Field Inspector', 'WorkplaceBigFive'),
  ('Communications Specialist', 'WorkplaceBigFive'),
  ('Investment Promotion Specialist', 'WorkplaceBigFive'),
  ('IT Support Engineer', 'WorkplaceBigFive'),
  ('Head of Section', 'WorkplaceBigFive'),
  ('Operations Manager', 'WorkplaceType16'),
  ('Business Analyst', 'WorkplaceType16'),
  ('HR Officer', 'WorkplaceType16'),
  ('Customer Service Officer', 'WorkplaceType16'),
  ('Software Developer', 'WorkplaceType16'),
  ('Policy and Economic Advisor', 'WorkplaceType16'),
  ('Legal Advisor', 'WorkplaceType16'),
  ('Accountant and Financial Analyst', 'WorkplaceType16'),
  ('Field Inspector', 'WorkplaceType16'),
  ('Communications Specialist', 'WorkplaceType16'),
  ('Investment Promotion Specialist', 'WorkplaceType16')
) AS o (NameEn, Model) ON o.NameEn = r.NameEn AND o.Model = r.PersonalityModel
WHERE r.UpdatedByStaffId IS NULL;
GO

INSERT INTO assessment.RoleProfile (NameAr, NameEn, PersonalityModel, Weights, Targets, Types, SortOrder, CreatedAt, UpdatedAt)
SELECT v.NameAr, v.NameEn, v.Model, v.Weights, v.Targets, v.Types, v.SortOrder, SYSUTCDATETIME(), SYSUTCDATETIME()
FROM (VALUES
 (N'القيادة والإدارة', N'Leadership and Management', 'WorkplaceBigFive', N'{"ES":25,"C":25,"E":20,"A":15,"O":15}', N'{"ES":"High","C":"High","E":"Moderate"}', '', 20),
 (N'القيادة والإدارة', N'Leadership and Management', 'WorkplaceType16', N'{}', N'{}', 'ENTJ,ESTJ,ENFJ,INTJ', 20),
 (N'السياسات والاقتصاد والاستراتيجية', N'Policy, Economics and Strategy', 'WorkplaceBigFive', N'{"O":30,"C":30,"ES":20,"A":10,"E":10}', N'{"O":"High","C":"High"}', '', 21),
 (N'السياسات والاقتصاد والاستراتيجية', N'Policy, Economics and Strategy', 'WorkplaceType16', N'{}', N'{}', 'INTJ,INTP,ENTJ,INFJ', 21),
 (N'الرقابة والترخيص والتفتيش', N'Regulation, Licensing and Inspection', 'WorkplaceBigFive', N'{"C":35,"ES":30,"A":15,"E":10,"O":10}', N'{"C":"High","ES":"High"}', '', 22),
 (N'الرقابة والترخيص والتفتيش', N'Regulation, Licensing and Inspection', 'WorkplaceType16', N'{}', N'{}', 'ISTJ,ESTJ,ISTP,ESTP', 22),
 (N'خدمة المتعاملين والشركاء', N'Customer and Partner Services', 'WorkplaceBigFive', N'{"A":30,"ES":30,"E":20,"C":15,"O":5}', N'{"A":"High","ES":"High"}', '', 23),
 (N'خدمة المتعاملين والشركاء', N'Customer and Partner Services', 'WorkplaceType16', N'{}', N'{}', 'ESFJ,ENFJ,ISFJ,ESFP', 23),
 (N'تطوير الأعمال والترويج السياحي والاستثماري', N'Business Development and Promotion', 'WorkplaceBigFive', N'{"E":30,"ES":25,"O":20,"A":15,"C":10}', N'{"E":"High","ES":"High"}', '', 24),
 (N'تطوير الأعمال والترويج السياحي والاستثماري', N'Business Development and Promotion', 'WorkplaceType16', N'{}', N'{}', 'ENTJ,ENTP,ESTP,ENFJ', 24),
 (N'التقنية والتحول الرقمي', N'Technology and Digital', 'WorkplaceBigFive', N'{"C":30,"O":30,"ES":20,"A":10,"E":10}', N'{"C":"High","O":"High"}', '', 25),
 (N'التقنية والتحول الرقمي', N'Technology and Digital', 'WorkplaceType16', N'{}', N'{}', 'INTP,INTJ,ISTP,ISTJ', 25),
 (N'البيانات والتحليل', N'Data and Analysis', 'WorkplaceBigFive', N'{"C":35,"O":30,"ES":20,"A":10,"E":5}', N'{"C":"High","O":"High"}', '', 26),
 (N'البيانات والتحليل', N'Data and Analysis', 'WorkplaceType16', N'{}', N'{}', 'INTJ,INTP,ISTJ,ISTP', 26),
 (N'الخدمات المساندة (الموارد البشرية والمالية والمشتريات والشؤون القانونية)', N'Corporate Support (HR, Finance, Procurement, Legal)', 'WorkplaceBigFive', N'{"C":40,"ES":25,"A":20,"E":10,"O":5}', N'{"C":"High"}', '', 27),
 (N'الخدمات المساندة (الموارد البشرية والمالية والمشتريات والشؤون القانونية)', N'Corporate Support (HR, Finance, Procurement, Legal)', 'WorkplaceType16', N'{}', N'{}', 'ISTJ,ESTJ,ISFJ,ESFJ', 27),
 (N'الاتصال والإعلام', N'Communications and Media', 'WorkplaceBigFive', N'{"E":30,"O":25,"A":20,"ES":15,"C":10}', N'{"E":"High"}', '', 28),
 (N'الاتصال والإعلام', N'Communications and Media', 'WorkplaceType16', N'{}', N'{}', 'ENFP,ENFJ,ENTP,ESFP', 28)
) AS v (NameAr, NameEn, Model, Weights, Targets, Types, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM assessment.RoleProfile r WHERE r.NameEn = v.NameEn AND r.PersonalityModel = v.Model);
GO
