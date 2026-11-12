using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Moet.Recruitment.Api.Endpoints;

public static class Json
{
    /// <summary>snake_case properties (dictionary keys such as wording keys are left as they are), Arabic kept readable.</summary>
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions o)
    {
        o.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        o.DictionaryKeyPolicy = null;
        o.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}

public static class Envelope
{
    public static object Error(string code, IReadOnlyDictionary<string, object?>? p) =>
        new { message = new { error = new { code, @params = p ?? new Dictionary<string, object?>() } } };
}
