/* Email server settings, email templates (system events and ready-made wording) and offer letter templates. */

CREATE TABLE ops.EmailSetting (
    Id                int            NOT NULL CONSTRAINT PK_EmailSetting PRIMARY KEY CONSTRAINT CK_EmailSetting_Single CHECK (Id = 1),
    Host              nvarchar(200)  NOT NULL CONSTRAINT DF_EmailSetting_Host DEFAULT (N''),
    Port              int            NOT NULL CONSTRAINT DF_EmailSetting_Port DEFAULT (587),
    UseTls            bit            NOT NULL CONSTRAINT DF_EmailSetting_UseTls DEFAULT (1),
    UserName          nvarchar(200)  NOT NULL CONSTRAINT DF_EmailSetting_UserName DEFAULT (N''),
    PasswordProtected nvarchar(1000) NOT NULL CONSTRAINT DF_EmailSetting_Password DEFAULT (N''),
    FromAddress       nvarchar(254)  NOT NULL CONSTRAINT DF_EmailSetting_From DEFAULT (N''),
    FromName          nvarchar(200)  NOT NULL CONSTRAINT DF_EmailSetting_FromName DEFAULT (N''),
    SignatureAr       nvarchar(1000) NOT NULL CONSTRAINT DF_EmailSetting_SignatureAr DEFAULT (N''),
    SignatureEn       nvarchar(1000) NOT NULL CONSTRAINT DF_EmailSetting_SignatureEn DEFAULT (N''),
    UpdatedAt         datetime2(0)   NOT NULL,
    UpdatedByStaffId  int            NULL CONSTRAINT FK_EmailSetting_Staff REFERENCES [identity].StaffUser (Id)
);
INSERT INTO ops.EmailSetting (Id, SignatureAr, SignatureEn, UpdatedAt) VALUES
 (1, COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'ar' AND TextKey = 'email.signature' AND ReviewStatus = 'Approved'), N'وزارة الاقتصاد والسياحة'), COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'en' AND TextKey = 'email.signature' AND ReviewStatus = 'Approved'), N'Ministry of Economy & Tourism'), SYSUTCDATETIME());
GO

CREATE TABLE ops.EmailTemplate (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmailTemplate PRIMARY KEY,
    [Key]            varchar(60)    NOT NULL CONSTRAINT UQ_EmailTemplate_Key UNIQUE,
    [Event]          varchar(40)    NULL,
    NameAr           nvarchar(150)  NOT NULL,
    NameEn           nvarchar(150)  NOT NULL,
    SubjectAr        nvarchar(300)  NOT NULL,
    BodyAr           nvarchar(max)  NOT NULL,
    SubjectEn        nvarchar(300)  NOT NULL,
    BodyEn           nvarchar(max)  NOT NULL,
    IsEnabled        bit            NOT NULL CONSTRAINT DF_EmailTemplate_IsEnabled DEFAULT (1),
    CreatedAt        datetime2(0)   NOT NULL,
    UpdatedAt        datetime2(0)   NOT NULL,
    UpdatedByStaffId int            NULL CONSTRAINT FK_EmailTemplate_Staff REFERENCES [identity].StaffUser (Id),
    RowVersion       rowversion     NOT NULL
);
/* One template per event. */
CREATE UNIQUE INDEX UX_EmailTemplate_Event ON ops.EmailTemplate ([Event]) WHERE [Event] IS NOT NULL;
GO

/* The four emails the portal already sent keep their wording (including HR's approved edits); new events start switched off. */
INSERT INTO ops.EmailTemplate ([Key], [Event], NameAr, NameEn, SubjectAr, BodyAr, SubjectEn, BodyEn, IsEnabled, CreatedAt, UpdatedAt) VALUES
 ('verify', 'verify', N'تفعيل الحساب', N'Account Activation', COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'ar' AND TextKey = 'email.verify_subject' AND ReviewStatus = 'Approved'), N'تفعيل حسابك في بوابة الوظائف'), COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'ar' AND TextKey = 'email.verify_body' AND ReviewStatus = 'Approved'), N'مرحبًا {name}،'+CHAR(10)+N''+CHAR(10)+N'يرجى تأكيد بريدك الإلكتروني لتفعيل حسابك في بوابة الوظائف بوزارة الاقتصاد والسياحة.'+CHAR(10)+N''+CHAR(10)+N'{link}'+CHAR(10)+N''+CHAR(10)+N'الرابط صالح لمدة 48 ساعة. إذا لم تقم بإنشاء هذا الحساب، يمكنك تجاهل هذه الرسالة.'), COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'en' AND TextKey = 'email.verify_subject' AND ReviewStatus = 'Approved'), N'Activate Your Careers Portal Account'), COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'en' AND TextKey = 'email.verify_body' AND ReviewStatus = 'Approved'), N'Hello {name},'+CHAR(10)+N''+CHAR(10)+N'Please confirm your email to activate your account on the Ministry of Economy & Tourism careers portal.'+CHAR(10)+N''+CHAR(10)+N'{link}'+CHAR(10)+N''+CHAR(10)+N'The link works for 48 hours. If you did not create this account, you can ignore this email.'), 1, SYSUTCDATETIME(), SYSUTCDATETIME()),
 ('reset', 'reset', N'إعادة تعيين كلمة المرور', N'Password Reset', COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'ar' AND TextKey = 'email.reset_subject' AND ReviewStatus = 'Approved'), N'إعادة تعيين كلمة مرور بوابة الوظائف'), COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'ar' AND TextKey = 'email.reset_body' AND ReviewStatus = 'Approved'), N'مرحبًا {name}،'+CHAR(10)+N''+CHAR(10)+N'تلقينا طلبًا لإعادة تعيين كلمة مرور حسابك في بوابة الوظائف. اختر كلمة مرور جديدة من هنا.'+CHAR(10)+N''+CHAR(10)+N'{link}'+CHAR(10)+N''+CHAR(10)+N'الرابط صالح لمدة ساعة واحدة ولمرة واحدة فقط. إذا لم تطلب ذلك، تجاهل هذه الرسالة وستبقى كلمة المرور كما هي.'), COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'en' AND TextKey = 'email.reset_subject' AND ReviewStatus = 'Approved'), N'Reset Your Careers Portal Password'), COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'en' AND TextKey = 'email.reset_body' AND ReviewStatus = 'Approved'), N'Hello {name},'+CHAR(10)+N''+CHAR(10)+N'We received a request to reset the password of your careers portal account. Choose a new password here.'+CHAR(10)+N''+CHAR(10)+N'{link}'+CHAR(10)+N''+CHAR(10)+N'The link works for one hour and can be used once. If you did not ask for this, ignore this email and your password stays the same.'), 1, SYSUTCDATETIME(), SYSUTCDATETIME()),
 ('assessment', 'assessment', N'دعوة إلى اختبار', N'Assessment Invitation', COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'ar' AND TextKey = 'email.assessment_subject' AND ReviewStatus = 'Approved'), N'دعوة إلى اختبار: {test}'), COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'ar' AND TextKey = 'email.assessment_body' AND ReviewStatus = 'Approved'), N'مرحبًا {name}،'+CHAR(10)+N''+CHAR(10)+N'ضمن طلبك للوظيفة، يرجى إكمال {test} عبر بوابة الوظائف بوزارة الاقتصاد والسياحة قبل {deadline} (بتوقيت الإمارات).'+CHAR(10)+N''+CHAR(10)+N'{link}'+CHAR(10)+N''+CHAR(10)+N'سجّل الدخول بحسابك في بوابة الوظائف للبدء. قبل أن تبدأ، تأكد من استقرار الاتصال ومن توفر الوقت الكافي لإكمال الاختبار.'), COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'en' AND TextKey = 'email.assessment_subject' AND ReviewStatus = 'Approved'), N'Assessment Invitation: {test}'), COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'en' AND TextKey = 'email.assessment_body' AND ReviewStatus = 'Approved'), N'Hello {name},'+CHAR(10)+N''+CHAR(10)+N'As part of your application, please complete the {test} on the Ministry of Economy & Tourism careers portal by {deadline} (UAE time).'+CHAR(10)+N''+CHAR(10)+N'{link}'+CHAR(10)+N''+CHAR(10)+N'Sign in with your careers portal account to start. Before you begin, make sure you have a stable connection and enough time to finish.'), 1, SYSUTCDATETIME(), SYSUTCDATETIME()),
 ('reexam', 'reexam', N'محاولة جديدة في اختبار', N'New Assessment Attempt', COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'ar' AND TextKey = 'email.reexam_subject' AND ReviewStatus = 'Approved'), N'محاولة جديدة: {test}'), COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'ar' AND TextKey = 'email.reexam_body' AND ReviewStatus = 'Approved'), N'مرحبًا {name}،'+CHAR(10)+N''+CHAR(10)+N'لقد مُنحت محاولة جديدة لأداء {test}. يرجى إكمالها قبل {deadline} (بتوقيت الإمارات).'+CHAR(10)+N''+CHAR(10)+N'{link}'), COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'en' AND TextKey = 'email.reexam_subject' AND ReviewStatus = 'Approved'), N'New Attempt: {test}'), COALESCE((SELECT [Text] FROM core.PortalText WHERE LanguageCode = 'en' AND TextKey = 'email.reexam_body' AND ReviewStatus = 'Approved'), N'Hello {name},'+CHAR(10)+N''+CHAR(10)+N'You have been given a new attempt at the {test}. Please complete it by {deadline} (UAE time).'+CHAR(10)+N''+CHAR(10)+N'{link}'), 1, SYSUTCDATETIME(), SYSUTCDATETIME()),
 ('application_received', 'application_received', N'استلام الطلب', N'Application Received', N'تم استلام طلبك: {job_title}', N'مرحبًا {name}،'+CHAR(10)+N''+CHAR(10)+N'شكرًا لتقديمك على وظيفة {job_title} في وزارة الاقتصاد والسياحة. الرقم المرجعي لطلبك هو {reference}.'+CHAR(10)+N''+CHAR(10)+N'يمكنك متابعة طلبك عبر بوابة الوظائف.'+CHAR(10)+N''+CHAR(10)+N'{link}', N'We Received Your Application: {job_title}', N'Hello {name},'+CHAR(10)+N''+CHAR(10)+N'Thank you for applying for {job_title} at the Ministry of Economy & Tourism. Your application reference is {reference}.'+CHAR(10)+N''+CHAR(10)+N'You can follow your application on the careers portal.'+CHAR(10)+N''+CHAR(10)+N'{link}', 0, SYSUTCDATETIME(), SYSUTCDATETIME()),
 ('interview_scheduled', 'interview_scheduled', N'موعد المقابلة', N'Interview Scheduled', N'دعوة إلى مقابلة: {job_title}', N'مرحبًا {name}،'+CHAR(10)+N''+CHAR(10)+N'يسعدنا دعوتك إلى مقابلة ({round}) لوظيفة {job_title}.'+CHAR(10)+N''+CHAR(10)+N'التاريخ: {date}'+CHAR(10)+N'الوقت: {time} (بتوقيت الإمارات)'+CHAR(10)+N'المكان: {location}'+CHAR(10)+N''+CHAR(10)+N'إذا تعذّر عليك الحضور في هذا الموعد، يرجى إبلاغنا بالرد على هذه الرسالة.', N'Interview Invitation: {job_title}', N'Hello {name},'+CHAR(10)+N''+CHAR(10)+N'We would like to invite you to an interview ({round}) for {job_title}.'+CHAR(10)+N''+CHAR(10)+N'Date: {date}'+CHAR(10)+N'Time: {time} (UAE time)'+CHAR(10)+N'Location: {location}'+CHAR(10)+N''+CHAR(10)+N'If you cannot attend at this time, please let us know by replying to this email.', 0, SYSUTCDATETIME(), SYSUTCDATETIME()),
 ('offer_sent', 'offer_sent', N'عرض العمل', N'Job Offer', N'عرض عمل: {position_title}', N'مرحبًا {name}،'+CHAR(10)+N''+CHAR(10)+N'يسرّنا أن نقدم لك عرضًا لشغل وظيفة {position_title}. تجد خطاب العرض أدناه.'+CHAR(10)+N''+CHAR(10)+N'{offer_letter}', N'Job Offer: {position_title}', N'Hello {name},'+CHAR(10)+N''+CHAR(10)+N'We are pleased to offer you the position of {position_title}. Your offer letter is below.'+CHAR(10)+N''+CHAR(10)+N'{offer_letter}', 0, SYSUTCDATETIME(), SYSUTCDATETIME()),
 ('not_selected', 'not_selected', N'عدم الاختيار', N'Not Selected', N'طلبك لوظيفة {job_title}', N'مرحبًا {name}،'+CHAR(10)+N''+CHAR(10)+N'نشكرك على اهتمامك بوظيفة {job_title} في وزارة الاقتصاد والسياحة. بعد دراسة متأنية، نود إبلاغك بأنه لن يتم المضي في طلبك (الرقم المرجعي {reference}) في الوقت الحالي.'+CHAR(10)+N''+CHAR(10)+N'نقدّر الوقت الذي خصصته، ونرحب بتقديمك على الفرص المستقبلية.', N'Your Application for {job_title}', N'Hello {name},'+CHAR(10)+N''+CHAR(10)+N'Thank you for your interest in {job_title} at the Ministry of Economy & Tourism. After careful consideration, we will not be moving forward with your application (reference {reference}) at this time.'+CHAR(10)+N''+CHAR(10)+N'We appreciate the time you invested and welcome your applications for future opportunities.', 0, SYSUTCDATETIME(), SYSUTCDATETIME());
GO

CREATE TABLE hiring.OfferTemplate (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OfferTemplate PRIMARY KEY,
    NameAr           nvarchar(150)  NOT NULL,
    NameEn           nvarchar(150)  NOT NULL,
    BodyAr           nvarchar(max)  NOT NULL,
    BodyEn           nvarchar(max)  NOT NULL,
    IsDefault        bit            NOT NULL CONSTRAINT DF_OfferTemplate_IsDefault DEFAULT (0),
    IsActive         bit            NOT NULL CONSTRAINT DF_OfferTemplate_IsActive DEFAULT (1),
    CreatedAt        datetime2(0)   NOT NULL,
    UpdatedAt        datetime2(0)   NOT NULL,
    UpdatedByStaffId int            NULL CONSTRAINT FK_OfferTemplate_Staff REFERENCES [identity].StaffUser (Id),
    RowVersion       rowversion     NOT NULL
);
CREATE UNIQUE INDEX UX_OfferTemplate_Default ON hiring.OfferTemplate (IsDefault) WHERE IsDefault = 1;
INSERT INTO hiring.OfferTemplate (NameAr, NameEn, BodyAr, BodyEn, IsDefault, CreatedAt, UpdatedAt) VALUES
 (N'خطاب العرض القياسي', N'Standard Offer Letter', N'# عرض عمل'+CHAR(10)+N''+CHAR(10)+N'التاريخ: {offer_date}'+CHAR(10)+N'الرقم المرجعي: {offer_code}'+CHAR(10)+N''+CHAR(10)+N'السيد/السيدة {name}،'+CHAR(10)+N''+CHAR(10)+N'إشارةً إلى طلبكم لوظيفة {job_title}، يسرّ وزارة الاقتصاد والسياحة أن تعرض عليكم شغل وظيفة {position_title} في {department}.'+CHAR(10)+N''+CHAR(10)+N'تاريخ المباشرة: {start_date}'+CHAR(10)+N'الراتب الشهري: {monthly_salary} {currency}'+CHAR(10)+N''+CHAR(10)+N'{terms}'+CHAR(10)+N''+CHAR(10)+N'يخضع هذا العرض لسياسات التوظيف في الوزارة واستكمال الإجراءات المطلوبة. يرجى تأكيد قبولكم بالرد على هذا الخطاب.'+CHAR(10)+N''+CHAR(10)+N'وزارة الاقتصاد والسياحة', N'# Offer of Employment'+CHAR(10)+N''+CHAR(10)+N'Date: {offer_date}'+CHAR(10)+N'Reference: {offer_code}'+CHAR(10)+N''+CHAR(10)+N'Dear {name},'+CHAR(10)+N''+CHAR(10)+N'Further to your application for {job_title}, the Ministry of Economy & Tourism is pleased to offer you the position of {position_title} in {department}.'+CHAR(10)+N''+CHAR(10)+N'Start date: {start_date}'+CHAR(10)+N'Monthly salary: {monthly_salary} {currency}'+CHAR(10)+N''+CHAR(10)+N'{terms}'+CHAR(10)+N''+CHAR(10)+N'This offer is subject to the Ministry''s employment policies and the completion of the required checks. Please confirm your acceptance by replying to this letter.'+CHAR(10)+N''+CHAR(10)+N'Ministry of Economy & Tourism', 1, SYSUTCDATETIME(), SYSUTCDATETIME());

ALTER TABLE hiring.JobOffer ADD OfferTemplateId int NULL CONSTRAINT FK_JobOffer_OfferTemplate REFERENCES hiring.OfferTemplate (Id);
GO
