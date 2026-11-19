/* Offer letter templates become document templates of any kind (offer letters, applicant letters, interview letters and
   whatever comes later). Existing templates keep their ids, so offers still point at them. */
EXEC sp_rename 'hiring.OfferTemplate', 'DocumentTemplate';
GO
ALTER SCHEMA core TRANSFER hiring.DocumentTemplate;
GO
DROP INDEX UX_OfferTemplate_Default ON core.DocumentTemplate;
ALTER TABLE core.DocumentTemplate ADD Kind varchar(30) NOT NULL CONSTRAINT DF_DocumentTemplate_Kind DEFAULT ('OfferLetter');
GO
CREATE UNIQUE INDEX UX_DocumentTemplate_Default ON core.DocumentTemplate (Kind) WHERE IsDefault = 1;

INSERT INTO core.DocumentTemplate (Kind, NameAr, NameEn, BodyAr, BodyEn, IsDefault, CreatedAt, UpdatedAt) VALUES
 ('InterviewLetter', N'خطاب دعوة إلى مقابلة', N'Interview Invitation Letter',
  N'# دعوة إلى مقابلة' + CHAR(10) + CHAR(10) + N'التاريخ: {today}' + CHAR(10) + N'الرقم المرجعي: {reference}' + CHAR(10) + CHAR(10) + N'السيد/السيدة {name}،' + CHAR(10) + CHAR(10)
   + N'يسر وزارة الاقتصاد والسياحة دعوتكم إلى مقابلة ({round}) لوظيفة {job_title}.' + CHAR(10) + CHAR(10) + N'التاريخ: {date}' + CHAR(10) + N'الوقت: {time} (بتوقيت الإمارات)' + CHAR(10) + N'المكان: {location}'
   + CHAR(10) + CHAR(10) + N'يرجى إحضار بطاقة الهوية وأصول الشهادات.' + CHAR(10) + CHAR(10) + N'وزارة الاقتصاد والسياحة',
  N'# Interview Invitation' + CHAR(10) + CHAR(10) + N'Date: {today}' + CHAR(10) + N'Reference: {reference}' + CHAR(10) + CHAR(10) + N'Dear {name},' + CHAR(10) + CHAR(10)
   + N'The Ministry of Economy & Tourism is pleased to invite you to an interview ({round}) for {job_title}.' + CHAR(10) + CHAR(10) + N'Date: {date}' + CHAR(10) + N'Time: {time} (UAE time)' + CHAR(10) + N'Location: {location}'
   + CHAR(10) + CHAR(10) + N'Please bring your Emirates ID or passport and your original certificates.' + CHAR(10) + CHAR(10) + N'Ministry of Economy & Tourism',
  1, SYSUTCDATETIME(), SYSUTCDATETIME()),
 ('ApplicantLetter', N'خطاب إفادة بالتقديم', N'Application Confirmation Letter',
  N'# إفادة' + CHAR(10) + CHAR(10) + N'التاريخ: {today}' + CHAR(10) + CHAR(10) + N'تفيد وزارة الاقتصاد والسياحة بأن السيد/السيدة {name} قد تقدّم/تقدّمت بطلب لوظيفة {job_title} بتاريخ {applied_on}، والرقم المرجعي للطلب هو {reference}.'
   + CHAR(10) + CHAR(10) + N'وقد أُعطيت هذه الإفادة بناءً على طلبه/طلبها.' + CHAR(10) + CHAR(10) + N'وزارة الاقتصاد والسياحة',
  N'# To Whom It May Concern' + CHAR(10) + CHAR(10) + N'Date: {today}' + CHAR(10) + CHAR(10) + N'This is to confirm that {name} applied for the position of {job_title} at the Ministry of Economy & Tourism on {applied_on}, with application reference {reference}.'
   + CHAR(10) + CHAR(10) + N'This letter is issued at their request.' + CHAR(10) + CHAR(10) + N'Ministry of Economy & Tourism',
  1, SYSUTCDATETIME(), SYSUTCDATETIME());
GO
