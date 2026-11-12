using System.Net.Http.Json;
using System.Text.Json;

namespace Moet.Recruitment.Agent.Backoffice;

/// <summary>
/// Calls the recruitment backoffice API as the staff member who is talking to the agent: the session cookie that
/// arrived with the AG-UI run is passed on, so the backend applies that person's role and records them in the audit
/// trail. The agent holds no credentials of its own.
/// </summary>
public sealed class BackofficeApi(IHttpClientFactory factory, IHttpContextAccessor context)
{
    public const string Client = "backoffice";

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>The language the staff member is using in the backoffice: "ar" (default) or "en".</summary>
    /// <remarks>The language of the user's latest message wins (they may write English in the Arabic backoffice).</remarks>
    public string Lang => context.HttpContext?.Items[Hosting.SettingsChatClient.MessageLangKey] as string
        ?? (context.HttpContext?.Request.Headers["X-Lang"].ToString() == "en" ? "en" : "ar");

    /// <summary>True the first time it is called with this key during the current run (one user message).</summary>
    public bool FirstInRun(string key)
    {
        var items = context.HttpContext?.Items;
        if (items == null) return true;
        if (items.ContainsKey("once:" + key)) return false;
        items["once:" + key] = true;
        return true;
    }

    public Task<JsonElement> GetAsync(string path, CancellationToken ct = default) => SendAsync(HttpMethod.Get, path, null, ct);

    public Task<JsonElement> PostAsync(string path, object body, CancellationToken ct = default) => SendAsync(HttpMethod.Post, path, body, ct);

    async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path.TrimStart('/'));
        req.Headers.TryAddWithoutValidation("X-Backoffice", "1");
        // Names and titles come back in the staff member's language (English job titles when they use English).
        req.Headers.TryAddWithoutValidation("X-Lang", Lang);
        var cookie = context.HttpContext?.Request.Headers.Cookie.ToString();
        if (!string.IsNullOrEmpty(cookie)) req.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (body != null) req.Content = JsonContent.Create(body, options: Json);

        using var res = await factory.CreateClient(Client).SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (res.IsSuccessStatusCode)
            return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();

        // Errors go back to the model as data so it can explain them or try something else.
        string code = $"http_{(int)res.StatusCode}";
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("code", out var c)) code = c.GetString() ?? code;
        }
        catch (JsonException) { }
        throw new BackofficeException(code, (int)res.StatusCode);
    }
}

public sealed class BackofficeException(string code, int status) : Exception($"Backoffice error {status}: {code}")
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
