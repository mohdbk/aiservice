/* Assistant conversations kept on the server (per staff member, optionally shared read-only with colleagues). */
CREATE TABLE ops.AgentConversation (
  Thread varchar(64) NOT NULL CONSTRAINT PK_AgentConversation PRIMARY KEY,
  StaffUserId int NOT NULL CONSTRAINT FK_AgentConversation_Staff REFERENCES [identity].StaffUser (Id),
  Title nvarchar(200) NOT NULL,
  Content nvarchar(max) NOT NULL,
  IsShared bit NOT NULL,
  CreatedAt datetime2(3) NOT NULL,
  UpdatedAt datetime2(3) NOT NULL
);
GO
CREATE INDEX IX_AgentConversation_Staff ON ops.AgentConversation (StaffUserId, UpdatedAt DESC);
GO
