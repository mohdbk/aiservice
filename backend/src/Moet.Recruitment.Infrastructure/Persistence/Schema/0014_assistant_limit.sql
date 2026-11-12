/* A person's own assistant messages per day (null: the assistant's default; 0: off for them). */
ALTER TABLE identity.StaffUser ADD AgentDailyLimit smallint NULL;
GO
