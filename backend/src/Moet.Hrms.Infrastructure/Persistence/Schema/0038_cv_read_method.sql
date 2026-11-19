/* How a saved CV was read (pdf-text, word, ocr, model), so scoring an application made with it can reuse its text and
   profile instead of reading the file again; and which saved CV each application was sent with. */
ALTER TABLE hiring.CandidateCv ADD ReadMethod varchar(20) NOT NULL CONSTRAINT DF_CandidateCv_ReadMethod DEFAULT ('');
ALTER TABLE hiring.CvDraft ADD ReadMethod varchar(20) NOT NULL CONSTRAINT DF_CvDraft_ReadMethod DEFAULT ('');
/* The saved CV an application was sent with (no foreign key: the application keeps its own copy of the file, so a saved
   CV can be deleted later; the id then simply finds nothing). */
ALTER TABLE hiring.JobApplication ADD CandidateCvId int NULL;
/* Suggested screening answers kept per saved CV and job, so reopening the application form asks the model once. */
ALTER TABLE hiring.CandidateCv ADD ScreeningSuggestionsJson nvarchar(max) NULL CONSTRAINT CK_CandidateCv_Suggestions CHECK (ScreeningSuggestionsJson IS NULL OR ISJSON(ScreeningSuggestionsJson) = 1);
GO
