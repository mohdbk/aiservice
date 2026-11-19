using System.Text.Json;
using System.Text.Json.Nodes;

namespace Moet.Recruitment.Agent.A2ui;

/// <summary>
/// Turns form fields (from the agent's ask_user call or from a component an admin designed in the backoffice) into
/// A2UI inputs bound to /form/{name} on a surface.
/// </summary>
public static class Forms
{
    public sealed record Field(string Name, string Label, string Input, IReadOnlyList<(string Value, string Label)> Options, bool Required, string? Value);

    /// <summary>Adds one input and its starting value; returns the component id. Required paths are added to <paramref name="required"/>.</summary>
    public static string Add(Surface s, JsonObject form, Field f, List<string> required)
    {
        var path = $"/form/{f.Name}";
        if (f.Required && f.Input != "checkbox") required.Add(path);
        switch (f.Input)
        {
            case "multichoice":
                form[f.Name] = new JsonArray([.. (f.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => (JsonNode)x)]);
                return s.ChoicePicker(f.Label, path, f.Options, true, f.Required);
            case "checkbox":
                form[f.Name] = f.Value is "true" or "yes" or "1";
                return s.CheckBox(f.Label, path);
        }
        form[f.Name] = f.Value ?? "";
        return f.Input switch
        {
            "longtext" => s.TextField(f.Label, path, "longText", f.Required),
            "number" => s.TextField(f.Label, path, "number", f.Required),
            "date" => s.DateTimeInput(f.Label, path, true, false, f.Required),
            "time" => s.DateTimeInput(f.Label, path, false, true, f.Required),
            "choice" => s.ChoicePicker(f.Label, path, f.Options, false, f.Required),
            _ => s.TextField(f.Label, path, "shortText", f.Required),
        };
    }

    /// <summary>
    /// Draws an admin-designed component's nodes (headings, text, dividers, fields and rows of fields). Choice options come
    /// from the component or from a named list (rounds, staff, tests, departments, statuses, employment types).
    /// </summary>
    public static List<string> Component(Surface s, JsonElement definition, string lang, Func<string, IReadOnlyList<(string, string)>> source,
        IReadOnlyDictionary<string, string?> values, JsonObject form, List<string> required)
    {
        string Text(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var t) || t.ValueKind != JsonValueKind.Object) return "";
            var mine = t.TryGetProperty(lang, out var v) ? v.GetString() ?? "" : "";
            return mine.Length > 0 ? mine : t.TryGetProperty(lang == "ar" ? "en" : "ar", out var o) ? o.GetString() ?? "" : "";
        }

        string FieldOf(JsonElement n)
        {
            var name = n.GetProperty("name").GetString()!;
            var input = n.TryGetProperty("input", out var i) ? i.GetString() ?? "text" : "text";
            IReadOnlyList<(string, string)> options = n.TryGetProperty("source", out var src) && src.GetString() is { Length: > 0 } listName
                ? source(listName)
                : n.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array
                    ? o.EnumerateArray().Select(x => (x.GetProperty("value").ToString(), Text(x, "label") is { Length: > 0 } l ? l : x.GetProperty("value").ToString())).ToList()
                    : [];
            var start = values.TryGetValue(name, out var given) && given != null ? given
                : n.TryGetProperty("default", out var d) && d.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? d.ToString() : null;
            var label = Text(n, "label");
            return Add(s, form, new Field(name, label.Length > 0 ? label : name, input, options, n.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True, start), required);
        }

        var ids = new List<string>();
        if (!definition.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array) return ids;
        foreach (var n in nodes.EnumerateArray())
        {
            switch (n.TryGetProperty("type", out var t) ? t.GetString() : "")
            {
                case "heading": ids.Add(s.Text(Text(n, "text"), "h5")); break;
                case "text": ids.Add(s.Text(Text(n, "text"), "caption")); break;
                case "divider": ids.Add(s.Divider()); break;
                case "field": ids.Add(FieldOf(n)); break;
                case "row":
                    ids.Add(s.Row(n.GetProperty("children").EnumerateArray().Where(k => k.TryGetProperty("type", out var kt) && kt.GetString() == "field").Select(FieldOf).ToList()));
                    break;
            }
        }
        return ids;
    }

    /// <summary>The component's text in the user's language: {"ar": "...", "en": "..."}.</summary>
    public static string Localized(JsonElement e, string name, string lang)
    {
        if (!e.TryGetProperty(name, out var t) || t.ValueKind != JsonValueKind.Object) return "";
        var mine = t.TryGetProperty(lang, out var v) ? v.GetString() ?? "" : "";
        return mine.Length > 0 ? mine : t.TryGetProperty(lang == "ar" ? "en" : "ar", out var o) ? o.GetString() ?? "" : "";
    }
}
