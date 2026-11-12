using System.Text.Json;
using System.Text.RegularExpressions;
using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Domain.Operations;

/// <summary>The changes the recruitment agent may propose, and the fields a person may edit on each before approving.</summary>
public static class AgentActions
{
    public static readonly IReadOnlyDictionary<string, string[]> Editable = new Dictionary<string, string[]>
    {
        ["set_applicant_status"] = ["status"],
        ["add_note"] = ["text"],
        ["schedule_interview"] = ["round_id", "date", "from", "to", "location", "notes", "panel_ids"],
        ["invite_to_test"] = ["assessment_id", "deadline_days"],
        ["make_offer"] = ["position_title", "offer_date", "start_date", "monthly_salary", "currency", "terms"],
        ["email_candidate"] = ["subject", "body"],
        ["create_draft_job"] = ["title", "location", "employment_type", "department_id"],
        ["suggest_english"] = [],
        ["offer_interview_times"] = ["round_id", "times", "location", "panel_ids"],
        ["talent_invite"] = ["job_id"],
        ["set_offer_status"] = ["status"],
        ["set_interview_status"] = ["status"],
        ["set_job_selection"] = ["role_profile_id", "w_cv", "w_tests", "w_interview", "w_personality"],
    };
}

/// <summary>
/// A form designed by HR admins in the backoffice's component builder. The agent draws it in the chat as an A2UI
/// surface: either a question form it can show when it needs details (Ask), or the editable card for one kind of
/// proposed change (Approval), which replaces the built-in card for that action.
/// </summary>
public class AgentComponent
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string NameEn { get; set; } = "";
    /// <summary>When the agent should use it, in plain words (read by the agent).</summary>
    public string Purpose { get; set; } = "";
    public ComponentKind Kind { get; set; }
    public string? ActionName { get; set; }
    public string Definition { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedByStaffId { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public void Validate()
    {
        if (!ComponentDefinition.KeyPattern().IsMatch(Key)) throw DomainException.Invalid("component_key_invalid");
        if (Name.Trim().Length == 0) throw DomainException.Invalid("name_required");
        if (Kind == ComponentKind.Approval)
        {
            if (ActionName == null || !AgentActions.Editable.ContainsKey(ActionName)) throw DomainException.Invalid("component_action_required");
        }
        else ActionName = null;
        ComponentDefinition.Validate(Definition, Kind == ComponentKind.Approval ? AgentActions.Editable[ActionName!] : null);
    }
}

public enum ComponentKind { Ask, Approval }

/// <summary>
/// Rules for a component's definition (JSON):
/// <code>
/// { "title": {"ar","en"}, "submit": {"ar","en"},
///   "nodes": [ {"type":"heading"|"text","text":{"ar","en"}}, {"type":"divider"},
///              {"type":"field","name","input","label":{"ar","en"},"required","default","options":[{"value","label":{"ar","en"}}],"source"},
///              {"type":"row","children":[field, field]} ] }
/// </code>
/// </summary>
public static partial class ComponentDefinition
{
    public const int MaxNodes = 40;
    public static readonly string[] Inputs = ["text", "longtext", "number", "date", "time", "choice", "multichoice", "checkbox"];
    public static readonly string[] Sources = ["rounds", "staff", "assessments", "departments", "statuses", "employment_types"];

    [GeneratedRegex("^[a-z][a-z0-9_]{1,59}$")]
    public static partial Regex KeyPattern();

    public static void Validate(string json, IReadOnlyCollection<string>? allowedFields)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { throw DomainException.Invalid("component_invalid"); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
                throw DomainException.Invalid("component_invalid");
            var names = new HashSet<string>();
            var count = 0;
            foreach (var n in nodes.EnumerateArray()) Node(n, names, allowedFields, ref count, nested: false);
            if (count > MaxNodes) throw DomainException.Invalid("component_too_big");
            if (names.Count == 0) throw DomainException.Invalid("component_needs_field");
        }
    }

    static void Node(JsonElement n, HashSet<string> names, IReadOnlyCollection<string>? allowed, ref int count, bool nested)
    {
        count++;
        var type = n.ValueKind == JsonValueKind.Object && n.TryGetProperty("type", out var t) ? t.GetString() : null;
        switch (type)
        {
            case "heading" or "text" when !nested:
            case "divider" when !nested:
                return;
            case "row" when !nested:
                if (!n.TryGetProperty("children", out var kids) || kids.ValueKind != JsonValueKind.Array || kids.GetArrayLength() is 0 or > 4)
                    throw DomainException.Invalid("component_row_invalid");
                foreach (var k in kids.EnumerateArray()) Node(k, names, allowed, ref count, nested: true);
                return;
            case "field":
            {
                var name = Str(n, "name");
                if (!KeyPattern().IsMatch(name) || !names.Add(name)) throw DomainException.Invalid("component_field_name", new Dictionary<string, object?> { ["name"] = name });
                if (allowed != null && !allowed.Contains(name)) throw DomainException.Invalid("component_field_not_editable", new Dictionary<string, object?> { ["name"] = name });
                var input = Str(n, "input");
                if (!Inputs.Contains(input)) throw DomainException.Invalid("component_input_invalid");
                if (input is "choice" or "multichoice")
                {
                    var source = Str(n, "source");
                    var hasOptions = n.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array && o.GetArrayLength() > 0;
                    if (source.Length > 0 ? !Sources.Contains(source) : !hasOptions) throw DomainException.Invalid("component_options_required", new Dictionary<string, object?> { ["name"] = name });
                }
                return;
            }
            default:
                throw DomainException.Invalid("component_invalid");
        }
    }

    static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
