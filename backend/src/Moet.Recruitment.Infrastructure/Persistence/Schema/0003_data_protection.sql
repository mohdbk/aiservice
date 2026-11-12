/* The ASP.NET Core data protection key ring (it protects staff sign-in cookies), kept in the database so sessions
   survive restarts and every API instance shares it. Rows are written by the framework; keys rotate every 90 days. */
CREATE TABLE ops.DataProtectionKey (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DataProtectionKey PRIMARY KEY,
    FriendlyName nvarchar(max)     NULL,
    Xml          nvarchar(max)     NULL
);
GO
