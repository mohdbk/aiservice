/* More starting role profiles for Ministry positions (HR tunes the weights, targets and types in the backoffice). Added only
   where a profile with the same English name and model does not exist yet. */
INSERT INTO assessment.RoleProfile (NameAr, NameEn, PersonalityModel, Weights, Targets, Types, SortOrder, CreatedAt, UpdatedAt)
SELECT v.NameAr, v.NameEn, v.Model, v.Weights, v.Targets, v.Types, v.SortOrder, SYSUTCDATETIME(), SYSUTCDATETIME()
FROM (VALUES
 (N'مدير عمليات', N'Operations Manager', 'WorkplaceBigFive', N'{"C":35,"ES":25,"E":15,"A":10,"O":15}', N'{"C":"High","ES":"High"}', '', 10),
 (N'محلل أعمال', N'Business Analyst', 'WorkplaceBigFive', N'{"C":30,"O":30,"ES":15,"A":15,"E":10}', N'{"C":"High","O":"High"}', '', 11),
 (N'أخصائي موارد بشرية', N'HR Officer', 'WorkplaceBigFive', N'{"A":30,"ES":25,"C":20,"E":15,"O":10}', N'{"A":"High","ES":"High"}', '', 12),
 (N'موظف خدمة متعاملين', N'Customer Service Officer', 'WorkplaceBigFive', N'{"A":30,"ES":30,"E":20,"C":15,"O":5}', N'{"A":"High","ES":"High"}', '', 13),
 (N'مطور برمجيات', N'Software Developer', 'WorkplaceBigFive', N'{"C":30,"O":30,"ES":20,"A":10,"E":10}', N'{"C":"High","O":"High"}', '', 14),
 (N'عالم بيانات', N'Data Scientist', 'WorkplaceBigFive', N'{"O":35,"C":30,"ES":20,"A":10,"E":5}', N'{"O":"High","C":"High"}', '', 15),
 (N'مستشار سياسات واقتصاد', N'Policy and Economic Advisor', 'WorkplaceBigFive', N'{"O":30,"C":30,"ES":20,"A":10,"E":10}', N'{"O":"High","C":"High"}', '', 16),
 (N'مستشار قانوني', N'Legal Advisor', 'WorkplaceBigFive', N'{"C":40,"ES":25,"O":15,"A":10,"E":10}', N'{"C":"VeryHigh","ES":"High"}', '', 17),
 (N'محاسب ومحلل مالي', N'Accountant and Financial Analyst', 'WorkplaceBigFive', N'{"C":45,"ES":25,"O":10,"A":10,"E":10}', N'{"C":"VeryHigh"}', '', 18),
 (N'أخصائي مشتريات', N'Procurement Specialist', 'WorkplaceBigFive', N'{"C":35,"ES":25,"A":15,"E":15,"O":10}', N'{"C":"High"}', '', 19),
 (N'مفتش ميداني', N'Field Inspector', 'WorkplaceBigFive', N'{"C":35,"ES":30,"A":15,"E":10,"O":10}', N'{"C":"High","ES":"High"}', '', 20),
 (N'أخصائي اتصال وإعلام', N'Communications Specialist', 'WorkplaceBigFive', N'{"E":30,"O":25,"A":20,"ES":15,"C":10}', N'{"E":"High"}', '', 21),
 (N'أخصائي ترويج الاستثمار', N'Investment Promotion Specialist', 'WorkplaceBigFive', N'{"E":30,"ES":25,"O":20,"A":15,"C":10}', N'{"E":"High","ES":"High"}', '', 22),
 (N'مهندس دعم تقني', N'IT Support Engineer', 'WorkplaceBigFive', N'{"C":30,"ES":25,"A":25,"O":10,"E":10}', N'{"C":"High","A":"Moderate"}', '', 23),
 (N'رئيس قسم', N'Head of Section', 'WorkplaceBigFive', N'{"ES":25,"C":25,"E":20,"A":15,"O":15}', N'{"ES":"High","C":"High","E":"Moderate"}', '', 24),
 (N'مدير عمليات', N'Operations Manager', 'WorkplaceType16', N'{}', N'{}', 'ESTJ,ENTJ,ISTJ,INTJ', 25),
 (N'محلل أعمال', N'Business Analyst', 'WorkplaceType16', N'{}', N'{}', 'INTJ,INTP,ISTJ,ENTJ', 26),
 (N'أخصائي موارد بشرية', N'HR Officer', 'WorkplaceType16', N'{}', N'{}', 'ENFJ,ESFJ,ISFJ,INFJ', 27),
 (N'موظف خدمة متعاملين', N'Customer Service Officer', 'WorkplaceType16', N'{}', N'{}', 'ESFJ,ENFJ,ESFP,ISFJ', 28),
 (N'مطور برمجيات', N'Software Developer', 'WorkplaceType16', N'{}', N'{}', 'INTP,INTJ,ISTP,ISTJ', 29),
 (N'مستشار سياسات واقتصاد', N'Policy and Economic Advisor', 'WorkplaceType16', N'{}', N'{}', 'INTJ,INTP,ENTJ,INFJ', 30),
 (N'مستشار قانوني', N'Legal Advisor', 'WorkplaceType16', N'{}', N'{}', 'ISTJ,INTJ,ESTJ,ENTJ', 31),
 (N'محاسب ومحلل مالي', N'Accountant and Financial Analyst', 'WorkplaceType16', N'{}', N'{}', 'ISTJ,ESTJ,INTJ,ISTP', 32),
 (N'مفتش ميداني', N'Field Inspector', 'WorkplaceType16', N'{}', N'{}', 'ISTJ,ESTJ,ISTP,ESTP', 33),
 (N'أخصائي اتصال وإعلام', N'Communications Specialist', 'WorkplaceType16', N'{}', N'{}', 'ENFP,ENFJ,ESFP,ENTP', 34),
 (N'أخصائي ترويج الاستثمار', N'Investment Promotion Specialist', 'WorkplaceType16', N'{}', N'{}', 'ENTJ,ESTP,ENTP,ENFJ', 35)
) AS v (NameAr, NameEn, Model, Weights, Targets, Types, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM assessment.RoleProfile r WHERE r.NameEn = v.NameEn AND r.PersonalityModel = v.Model);
GO
