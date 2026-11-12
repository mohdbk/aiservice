using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Ai;

public sealed record AgentSettingsInput(
    bool IsEnabled, string? ModelBaseUrl, string? ModelName, string? ApiKey, bool ClearApiKey, decimal? Temperature, int MaxSteps,
    string? ExtraInstructions, IReadOnlyList<string>? DisabledTools, int ProposalMinutes, int DailyRunLimit, int HistoryTurns,
    string? ExamplesAr, string? ExamplesEn);

/// <summary>The assistant's settings: edited by recruitment admins, read by the chat page and by the agent service.</summary>
public sealed class AgentSettingsService(IAppDbContext db, ICurrentActor actor, IClock clock, ISecretProtector secrets)
{
    public async Task<AgentSetting> CurrentAsync(CancellationToken ct = default)
    {
        // The schema script creates the row; this only covers a database where it was removed by hand.
        return await db.AgentSettings.FirstOrDefaultAsync(ct) ?? new AgentSetting { UpdatedAt = clock.UtcNow };
    }

    /// <summary>For the settings page. The model key is never returned, only whether one is stored.</summary>
    public async Task<object> GetAsync(CancellationToken ct = default)
    {
        var s = await CurrentAsync(ct);
        var since = clock.UtcNow.AddDays(-1);
        return new
        {
            is_enabled = s.IsEnabled, model_base_url = s.ModelBaseUrl, model_name = s.ModelName, has_api_key = s.ModelApiKeyProtected.Length > 0,
            can_store_key = secrets.IsConfigured, temperature = s.Temperature, max_steps = (int)s.MaxSteps, extra_instructions = s.ExtraInstructions,
            disabled_tools = s.DisabledToolList(), tools = AgentSetting.Tools, proposal_minutes = (int)s.ProposalMinutes, daily_run_limit = (int)s.DailyRunLimit,
            history_turns = (int)s.HistoryTurns, examples_ar = s.ExamplesAr, examples_en = s.ExamplesEn, updated_at = s.UpdatedAt,
            usage = new
            {
                runs_today = await db.AgentRuns.CountAsync(r => r.StartedAt >= since, ct),
                people_today = await db.AgentRuns.Where(r => r.StartedAt >= since).Select(r => r.StaffUserId).Distinct().CountAsync(ct),
                approved_today = await db.AgentProposals.CountAsync(p => p.DecidedAt >= since && p.Status == ProposalStatus.Approved, ct),
            },
        };
    }

    public async Task<object> SaveAsync(AgentSettingsInput input, CancellationToken ct = default)
    {
        var s = await CurrentAsync(ct);
        if (db.AgentSettings.Local.All(x => x != s)) db.AgentSettings.Add(s);
        s.IsEnabled = input.IsEnabled;
        s.ModelBaseUrl = TextUtil.Clip(input.ModelBaseUrl?.Trim(), 300);
        s.ModelName = TextUtil.Clip(input.ModelName?.Trim(), 100);
        if (input.ClearApiKey) s.ModelApiKeyProtected = "";
        else if (!string.IsNullOrWhiteSpace(input.ApiKey)) s.ModelApiKeyProtected = secrets.Protect(input.ApiKey.Trim());
        s.Temperature = input.Temperature;
        s.MaxSteps = (byte)Math.Clamp(input.MaxSteps, 0, 255);
        s.ExtraInstructions = TextUtil.NoEmDash(TextUtil.Clip(input.ExtraInstructions?.Trim(), 4000));
        s.SetDisabledTools(input.DisabledTools);
        s.ProposalMinutes = (short)Math.Clamp(input.ProposalMinutes, 0, short.MaxValue);
        s.DailyRunLimit = (short)Math.Clamp(input.DailyRunLimit, 0, short.MaxValue);
        s.HistoryTurns = (byte)Math.Clamp(input.HistoryTurns, 0, 255);
        s.ExamplesAr = TextUtil.NoEmDash(TextUtil.Clip(input.ExamplesAr?.Trim(), 2000));
        s.ExamplesEn = TextUtil.NoEmDash(TextUtil.Clip(input.ExamplesEn?.Trim(), 2000));
        s.UpdatedAt = clock.UtcNow;
        s.UpdatedByStaffId = actor.Id;
        s.Validate();
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    /// <summary>For the chat page: whether it is on, the example questions and how much history to send.</summary>
    public async Task<object> InfoAsync(CancellationToken ct = default)
    {
        var s = await CurrentAsync(ct);
        return new { is_enabled = s.IsEnabled, examples_ar = s.ExamplesAr, examples_en = s.ExamplesEn, history_turns = (int)s.HistoryTurns };
    }

    /// <summary>For the agent service only (shared key): everything it needs for a run, including the model key.</summary>
    public async Task<object> ForAgentAsync(CancellationToken ct = default)
    {
        var s = await CurrentAsync(ct);
        return new
        {
            is_enabled = s.IsEnabled, model_base_url = s.ModelBaseUrl, model_name = s.ModelName,
            model_api_key = s.ModelApiKeyProtected.Length > 0 ? secrets.Unprotect(s.ModelApiKeyProtected) ?? "" : "",
            temperature = s.Temperature, max_steps = (int)s.MaxSteps, extra_instructions = s.ExtraInstructions, disabled_tools = s.DisabledToolList(),
        };
    }

    /// <summary>Checks the assistant is on and the staff member is within today's limit, then records the run.</summary>
    public async Task StartRunAsync(CancellationToken ct = default)
    {
        var staffId = actor.Id ?? throw DomainException.Forbidden();
        var s = await CurrentAsync(ct);
        if (!s.IsEnabled) throw DomainException.Forbidden("assistant_off");
        var since = clock.UtcNow.AddDays(-1);
        if (await db.AgentRuns.CountAsync(r => r.StaffUserId == staffId && r.StartedAt >= since, ct) >= s.DailyRunLimit)
            throw new DomainException("too_many_messages", 429);
        db.AgentRuns.Add(new AgentRun { StaffUserId = staffId, StartedAt = clock.UtcNow });
        await db.SaveChangesAsync(ct);
    }
}
