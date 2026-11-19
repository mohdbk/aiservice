/* How a saved CV was read (pdf-text, word, ocr, model), so scoring an application made with it can reuse its text and
   profile instead of reading the file again. */
ALTER TABLE hiring.CandidateCv ADD ReadMethod varchar(20) NOT NULL CONSTRAINT DF_CandidateCv_ReadMethod DEFAULT ('');
ALTER TABLE hiring.CvDraft ADD ReadMethod varchar(20) NOT NULL CONSTRAINT DF_CvDraft_ReadMethod DEFAULT ('');
GO
