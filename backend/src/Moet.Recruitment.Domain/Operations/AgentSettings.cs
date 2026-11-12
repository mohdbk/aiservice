using System.Text.Json;
using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Domain.Operations;

/// <summary>How the recruitment agent behaves. One row, edited by recruitment admins; the agent reads it on every run.</summary>
public class AgentSetting
{
    public static readonly string[] Tools =
    [
        "list_jobs", "get_job", "search_applicants", "get_applicant", "list_interviews", "list_offers", "list_tests", "list_test_invitations",
        "get_lookups", "get_dashboard", "show_applicants", "compare_applicants", "ask_user", "list_components", "show_component", "list_email_templates",
        "propose_status_change", "propose_note", "propose_interview", "propose_test_invitation", "propose_offer", "propose_email",
        "propose_draft_job", "propose_english_suggestion",
    ];

    public byte Id { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    /// <summary>OpenAI-compatible endpoint; empty uses the agent service's LLM_BASE_URL.</summary>
    public string ModelBaseUrl { get; set; } = "";
    /// <summary>Empty uses the agent service's LLM_MODEL.</summary>
    public string ModelName { get; set; } = "";
    /// <summary>Encrypted at rest; empty uses the agent service's LLM_API_KEY.</summary>
    public string ModelApiKeyProtected { get; set; } = "";
    public decimal? Temperature { get; set; }
    public byte MaxSteps { get; set; } = 8;
    public string ExtraInstructions { get; set; } = "";
    public string DisabledTools { get; set; } = "[]";
    public short ProposalMinutes { get; set; } = 30;
    public short DailyRunLimit { get; set; } = 200;
    public byte HistoryTurns { get; set; } = 30;
    public string ExamplesAr { get; set; } = "";
    public string ExamplesEn { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedByStaffId { get; set; }

    public IReadOnlyList<string> DisabledToolList() =>
        JsonSerializer.Deserialize<List<string>>(DisabledTools) ?? [];

    public void SetDisabledTools(IEnumerable<string>? tools) =>
        DisabledTools = JsonSerializer.Serialize((tools ?? []).Where(Tools.Contains).Distinct().OrderBy(x => x).ToList());

    public void Validate()
    {
        if (ModelBaseUrl.Length > 0 && !(Uri.TryCreate(ModelBaseUrl, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"))
            throw DomainException.Invalid("url_invalid");
        if (Temperature is < 0 or > 2) throw DomainException.Invalid("agent_temperature");
        if (MaxSteps is < 1 or > 20) throw DomainException.Invalid("agent_max_steps");
        if (ProposalMinutes is < 5 or > 1440) throw DomainException.Invalid("agent_proposal_minutes");
        if (DailyRunLimit is < 1 or > 5000) throw DomainException.Invalid("agent_daily_limit");
        if (HistoryTurns is < 2 or > 100) throw DomainException.Invalid("agent_history_turns");
    }
}

public class AgentRun
{
    public long Id { get; set; }
    public int StaffUserId { get; set; }
    public DateTime StartedAt { get; set; }
}
