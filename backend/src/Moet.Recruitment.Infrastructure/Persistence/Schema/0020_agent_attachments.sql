/* Files attached in the assistant chat (their content is in ops.StoredFile; the read text is kept once read). */
CREATE TABLE ops.AgentAttachment (
  Id uniqueidentifier NOT NULL CONSTRAINT PK_AgentAttachment PRIMARY KEY,
  StaffUserId int NOT NULL CONSTRAINT FK_AgentAttachment_Staff REFERENCES [identity].StaffUser (Id),
  FileName nvarchar(200) NOT NULL,
  FileId uniqueidentifier NOT NULL CONSTRAINT FK_AgentAttachment_File REFERENCES ops.StoredFile (Id),
  Text nvarchar(max) NULL,
  ReadMethod varchar(30) NOT NULL,
  CreatedAt datetime2(3) NOT NULL
);
GO
