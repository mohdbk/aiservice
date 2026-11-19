/* Language model use, one row per call: where it came from (the API's own features or the assistant), the feature,
   the model and the tokens in and out, so admins can see use and an estimated cost. */
CREATE TABLE ops.ModelUsage (
    Id           bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ModelUsage PRIMARY KEY,
    At           datetime2(0)  NOT NULL,
    Source       varchar(20)   NOT NULL,
    Feature      varchar(60)   NOT NULL,
    Model        nvarchar(120) NOT NULL,
    InputTokens  int           NOT NULL,
    OutputTokens int           NOT NULL,
    StaffUserId  int           NULL
);
CREATE INDEX IX_ModelUsage_At ON ops.ModelUsage (At) INCLUDE (Source, Feature, InputTokens, OutputTokens);
GO
