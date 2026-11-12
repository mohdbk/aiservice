using System.Text.Json.Nodes;

namespace Moet.Recruitment.Agent.A2ui;

/// <summary>
/// Builds A2UI v1.0 messages (createSurface, updateComponents, updateDataModel) with the basic catalog. The agent
/// returns them inside tool results; the backoffice renders them with the UAE Design System and sends button events
/// back as A2UI actions.
/// </summary>
public sealed class Surface(string id)
{
    public const string Version = "1.0";
    public const string Catalog = "https://a2ui.org/specification/v1_0/catalogs/basic/catalog.json";

    readonly JsonArray components = [];
    readonly JsonObject data = [];
    int next;

    public string Id { get; } = id;

    string Add(string component, JsonObject props, string? id = null)
    {
        id ??= $"c{++next}";
        props["id"] = id;
        props["component"] = component;
        components.Add(props);
        return id;
    }

    public string Text(string text, string variant = "body", string? id = null) =>
        Add("Text", new JsonObject { ["text"] = text, ["variant"] = variant }, id);

    /// <summary>Text bound to a data model path, so it can change without new components.</summary>
    public string BoundText(string path, string variant = "body", string? id = null) =>
        Add("Text", new JsonObject { ["text"] = new JsonObject { ["@path"] = path }, ["variant"] = variant }, id);

    public string Column(IEnumerable<string> children, string? id = null, string align = "stretch") =>
        Add("Column", new JsonObject { ["children"] = new JsonArray([.. children.Select(c => (JsonNode)c)]), ["align"] = align }, id);

    public string Row(IEnumerable<string> children, string? id = null, string justify = "start") =>
        Add("Row", new JsonObject { ["children"] = new JsonArray([.. children.Select(c => (JsonNode)c)]), ["justify"] = justify, ["align"] = "center" }, id);

    public string Card(string child, string? id = null) => Add("Card", new JsonObject { ["child"] = child }, id);

    public string Divider() => Add("Divider", new JsonObject { ["axis"] = "horizontal" });

    public string Button(string label, string eventName, JsonObject context, string variant = "primary", string? id = null)
    {
        var text = Text(label);
        return Add("Button", new JsonObject
        {
            ["child"] = text, ["variant"] = variant,
            ["action"] = new JsonObject { ["event"] = new JsonObject { ["name"] = eventName, ["context"] = context } },
        }, id);
    }

    public Surface Set(string key, JsonNode? value)
    {
        data[key] = value;
        return this;
    }

    /// <summary>The messages that draw this surface: create it with its data, then send the components (root is "root").</summary>
    public JsonArray Messages() =>
    [
        new JsonObject
        {
            ["version"] = Version,
            ["createSurface"] = new JsonObject { ["surfaceId"] = Id, ["catalogId"] = Catalog, ["sendDataModel"] = true, ["dataModel"] = data.DeepClone() },
        },
        new JsonObject
        {
            ["version"] = Version,
            ["updateComponents"] = new JsonObject { ["surfaceId"] = Id, ["components"] = components.DeepClone() },
        },
    ];
}
