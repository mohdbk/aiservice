/* The ministry's time zone, chosen in Settings (empty: TIME_ZONE or Asia/Dubai). */
ALTER TABLE core.PortalSetting ADD TimeZone varchar(64) NOT NULL CONSTRAINT DF_PortalSetting_TimeZone DEFAULT ('');
GO
