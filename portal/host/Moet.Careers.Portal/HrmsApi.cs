using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Moet.Careers.Portal;

/// <summary>An error from the recruitment API (its code and parameters), or "server" when it could not be reached.</summary>
public sealed class ApiError(string code, int status = 502, JsonNode? parameters = null) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public JsonNode? Parameters { get; } = parameters;
}

/// <summary>A file sent on to the API (a CV or a test answer file).</summary>
public sealed record UploadFile(string Name, byte[] Data, string Type);

/// <summary>
/// Client for the recruitment API's portal endpoints (/api/portal/{method}) on the internal network, with the portal's
/// service key. Each answer is {"message": ...}; errors are {"message": {"error": {"code", "params"}}}.
/// </summary>
public sealed class HrmsApi(HttpClient http, PortalSettings settings, ILogger<HrmsApi> log)
{
    public const string Client = "recruitment";

    string Url(string method, IEnumerable<KeyValuePair<string, object?>>? query)
    {
        var url = $"{settings.ApiUrl}/api/portal/{method}";
        if (query == null) return url;
        var parts = query.Where(p => p.Value is not null && !(p.Value is string str && str.Length == 0))
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "")}");
        var qs = string.Join("&", parts);
        return qs.Length == 0 ? url : url + "?" + qs;
    }

    HttpRequestMessage Request(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.TryAddWithoutValidation("Authorization", $"token {settings.ApiKey}:{settings.ApiSecret}");
        return req;
    }

    async Task<JsonNode?> SendAsync(string method, HttpRequestMessage req, int? timeoutMs, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs ?? settings.ApiTimeoutMs);
        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, cts.Token);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            log.LogError("recruitment API {Method} unreachable: {Error}", method, e.Message);
            throw new ApiError("server");
        }
        using (res)
        {
            JsonNode? body = null;
            try { body = JsonNode.Parse(await res.Content.ReadAsStringAsync(cts.Token)); } catch (Exception) { /* not JSON */ }
            var msg = body is JsonObject o ? o["message"] : null;
            if (msg is JsonObject m && m["error"] is JsonObject err && err["code"] is JsonValue code && code.TryGetValue<string>(out var c) && c.Length > 0)
                throw new ApiError(c, (int)res.StatusCode >= 400 ? (int)res.StatusCode : 400, err["params"]?.DeepClone() ?? new JsonObject());
            if (!res.IsSuccessStatusCode)
            {
                log.LogError("recruitment API {Method} failed: HTTP {Status}", method, (int)res.StatusCode);
                throw new ApiError("server");
            }
            return msg?.DeepClone();
        }
    }

    public Task<JsonNode?> GetAsync(string method, Dictionary<string, object?> query, CancellationToken ct) =>
        SendAsync(method, Request(HttpMethod.Get, Url(method, query)), null, ct);

    public Task<JsonNode?> PostAsync(string method, JsonObject json, CancellationToken ct)
    {
        var req = Request(HttpMethod.Post, Url(method, null));
        req.Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json");
        return SendAsync(method, req, null, ct);
    }

    /// <summary>A multipart form (fields as text, plus an optional file), as the API's upload methods take.</summary>
    public Task<JsonNode?> PostFormAsync(string method, IEnumerable<KeyValuePair<string, string>> fields, string fileField, UploadFile? file, CancellationToken ct)
    {
        var form = new MultipartFormDataContent();
        foreach (var (k, v) in fields) form.Add(new StringContent(v, Encoding.UTF8), k);
        if (file != null)
        {
            var part = new ByteArrayContent(file.Data);
            part.Headers.ContentType = MediaTypeHeaderValue.TryParse(file.Type, out var t) ? t : new MediaTypeHeaderValue("application/octet-stream");
            form.Add(part, fileField, file.Name);
        }
        var req = Request(HttpMethod.Post, Url(method, null));
        req.Content = form;
        return SendAsync(method, req, 120_000, ct);
    }

    /// <summary>The ministry logo from the API (an image, passed on as it is).</summary>
    public async Task<(byte[] Data, string Type)> LogoAsync(bool dark, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(settings.ApiTimeoutMs);
        try
        {
            using var res = await http.SendAsync(Request(HttpMethod.Get, Url("logo", new Dictionary<string, object?> { ["dark"] = dark ? 1 : 0 })), cts.Token);
            if (!res.IsSuccessStatusCode) throw new ApiError(res.StatusCode == System.Net.HttpStatusCode.NotFound ? "not_found" : "server", res.StatusCode == System.Net.HttpStatusCode.NotFound ? 404 : 502);
            return (await res.Content.ReadAsByteArrayAsync(cts.Token), res.Content.Headers.ContentType?.ToString() ?? "application/octet-stream");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { throw new ApiError("server"); }
    }
}
