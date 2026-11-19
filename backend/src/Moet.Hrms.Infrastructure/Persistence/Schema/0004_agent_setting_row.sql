/* The assistant settings row exists from the start, so first use never races to create it. */
IF NOT EXISTS (SELECT 1 FROM ops.AgentSetting WHERE Id = 1)
    INSERT INTO ops.AgentSetting (Id, IsEnabled, ModelBaseUrl, ModelName, ModelApiKeyProtected, Temperature, MaxSteps, ExtraInstructions,
        DisabledTools, ProposalMinutes, DailyRunLimit, HistoryTurns, ExamplesAr, ExamplesEn, UpdatedAt, UpdatedByStaffId)
    VALUES (1, 1, N'', N'', N'', NULL, 8, N'', N'[]', 30, 200, 30, N'', N'', SYSUTCDATETIME(), NULL);
GO
