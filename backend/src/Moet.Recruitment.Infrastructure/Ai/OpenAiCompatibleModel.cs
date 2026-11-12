using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Moet.Recruitment.Application.Common;

namespace Moet.Recruitment.Infrastructure.Ai;

/// <summary>
/// Any OpenAI-compatible chat endpoint: vLLM, Ollama or LiteLLM on-prem, or a hosted provider.
/// Settings: LLM_BASE_URL, LLM_MODEL, LLM_API_KEY (optional for on-prem servers).
/// </summary>
public sealed partial class OpenAiCompatibleModel(HttpClient http, IConfiguration cfg) : ILanguageModel
{
    [GeneratedRegex(@"\{[\s\S]*\}")] private static partial Regex ObjectRe();
    [GeneratedRegex(@"<think>[\s\S]*?</think>")] private static partial Regex ThinkRe();

    string Base => (cfg["LLM_BASE_URL"] ?? "").Trim().TrimEnd('/');
    string Key => cfg["LLM_API_KEY"] ?? "";
    bool IsOpenRouter => Base.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase);

    public bool IsConfigured => Base.Length > 0 && (!IsOpenRouter || Key.Length > 0);

    public string ModelName => cfg["LLM_MODEL"] ?? "";

    public async Task<JsonElement> CompleteJsonAsync(string system, string user, object schema, string schemaName, CancellationToken ct = default)
    {
        var body = Body([new JsonObject { ["role"] = "system", ["content"] = system }, new JsonObject { ["role"] = "user", ["content"] = user }], 0.1);
        body["response_format"] = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject { ["name"] = schemaName, ["strict"] = true, ["schema"] = JsonSerializer.SerializeToNode(schema) },
        };
        var data = await PostAsync(body, ct);
        var content = data["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "";
        return ParseJson(content);
    }

    public async Task<ChatTurn> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec> tools, CancellationToken ct = default)
    {
        var arr = new JsonArray();
        foreach (var m in messages)
        {
            var o = new JsonObject { ["role"] = m.Role, ["content"] = m.Content ?? "" };
            if (m.ToolCalls is { Count: > 0 })
                o["tool_calls"] = new JsonArray(m.ToolCalls.Select(c => (JsonNode)new JsonObject
                {
                    ["id"] = c.Id, ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments },
                }).ToArray());
            if (m.ToolCallId != null) o["tool_call_id"] = m.ToolCallId;
            arr.Add(o);
        }
        var body = Body([.. arr.Select(n => n!.DeepClone())], 0.2);
        if (tools.Count > 0)
        {
            body["tools"] = new JsonArray(tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = JsonSerializer.SerializeToNode(t.Parameters) },
            }).ToArray());
            body["tool_choice"] = "auto";
        }
        var data = await PostAsync(body, ct);
        var msg = data["choices"]?[0]?["message"];
        var calls = (msg?["tool_calls"] as JsonArray ?? []).Select(c => new ToolCall(
            c?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
            c?["function"]?["name"]?.GetValue<string>() ?? "",
            c?["function"]?["arguments"]?.GetValue<string>() ?? "{}")).ToList();
        var usage = data["usage"];
        return new ChatTurn(Strip(msg?["content"]?.GetValue<string>()), calls,
            usage?["prompt_tokens"]?.GetValue<int>() ?? 0, usage?["completion_tokens"]?.GetValue<int>() ?? 0);
    }

    JsonObject Body(JsonNode[] messages, double temperature)
    {
        var body = new JsonObject { ["model"] = ModelName, ["messages"] = new JsonArray(messages), ["temperature"] = temperature };
        if (IsOpenRouter)
        {
            body["provider"] = new JsonObject { ["require_parameters"] = true, ["data_collection"] = "deny" };
            body["reasoning"] = new JsonObject { ["enabled"] = false };
        }
        return body;
    }

    async Task<JsonNode> PostAsync(JsonObject body, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("The language model is not configured (LLM_BASE_URL, LLM_MODEL).");
        var res = await SendAsync(body, ct);
        if ((int)res.StatusCode == 400)
        {
            // Some servers reject strict schemas or provider options: retry with plain JSON mode.
            body.Remove("reasoning");
            if (body.ContainsKey("response_format")) body["response_format"] = new JsonObject { ["type"] = "json_object" };
            res = await SendAsync(body, ct);
        }
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Language model error {(int)res.StatusCode}: {text[..Math.Min(300, text.Length)]}");
        return JsonNode.Parse(text) ?? throw new InvalidOperationException("Empty language model response");
    }

    async Task<HttpResponseMessage> SendAsync(JsonObject body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (Key.Length > 0) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        return await http.SendAsync(req, ct);
    }

    static string? Strip(string? s) => s == null ? null : ThinkRe().Replace(s, "").Trim();

    /// <summary>The first JSON object in the reply (models sometimes wrap it in text or code fences).</summary>
    internal static JsonElement ParseJson(string content)
    {
        var s = Strip(content) ?? "";
        try { return JsonDocument.Parse(s).RootElement.Clone(); }
        catch (JsonException)
        {
            var m = ObjectRe().Match(s);
            if (!m.Success) throw new InvalidOperationException("The language model did not return JSON.");
            return JsonDocument.Parse(m.Value).RootElement.Clone();
        }
    }
}
