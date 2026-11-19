using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Moet.Hrms.Application.Common;

namespace Moet.Hrms.Infrastructure.Ai;

/// <summary>
/// Any OpenAI-compatible chat endpoint: vLLM, Ollama or LiteLLM on-prem, or a hosted provider.
/// Settings: LLM_BASE_URL, LLM_MODEL, LLM_API_KEY (optional for on-prem servers).
/// </summary>
public sealed partial class OpenAiCompatibleModel(HttpClient http, IConfiguration cfg, IModelUsageLog? usageLog = null) : ILanguageModel
{
    [GeneratedRegex(@"\{[\s\S]*\}")] private static partial Regex ObjectRe();
    [GeneratedRegex(@"<think>[\s\S]*?</think>")] private static partial Regex ThinkRe();

    string Base => (cfg["LLM_BASE_URL"] ?? "").Trim().TrimEnd('/');
    string Key => cfg["LLM_API_KEY"] ?? "";
    bool IsOpenRouter => Base.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase);

    public bool IsConfigured => Base.Length > 0 && (!IsOpenRouter || Key.Length > 0);

    public string ModelName => cfg["LLM_MODEL"] ?? "";

    /// <summary>CV scoring can use a stronger model than the rest (LLM_SCORE_MODEL); reading CVs and drafts keep the main one.</summary>
    public string ScoringModelName => cfg["LLM_SCORE_MODEL"] is { Length: > 0 } m ? m : ModelName;

    public async Task<JsonElement> CompleteJsonAsync(string system, string user, object schema, string schemaName, CancellationToken ct = default)
    {
        var modelName = schemaName == "cv_score" ? ScoringModelName : ModelName;
        var body = Body([new JsonObject { ["role"] = "system", ["content"] = system }, new JsonObject { ["role"] = "user", ["content"] = user }], 0.1, modelName);
        body["response_format"] = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject { ["name"] = schemaName, ["strict"] = true, ["schema"] = JsonSerializer.SerializeToNode(schema) },
        };
        var data = await PostAsync(body, ct);
        await RecordAsync(data, schemaName, modelName);
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
        await RecordAsync(data, "chat", ModelName);
        var msg = data["choices"]?[0]?["message"];
        var calls = (msg?["tool_calls"] as JsonArray ?? []).Select(c => new ToolCall(
            c?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
            c?["function"]?["name"]?.GetValue<string>() ?? "",
            c?["function"]?["arguments"]?.GetValue<string>() ?? "{}")).ToList();
        var usage = data["usage"];
        return new ChatTurn(Strip(msg?["content"]?.GetValue<string>()), calls,
            usage?["prompt_tokens"]?.GetValue<int>() ?? 0, usage?["completion_tokens"]?.GetValue<int>() ?? 0);
    }

    /// <summary>
    /// Reads the text of CV images with a model that accepts images (LLM_VISION_MODEL, or the main model), when no OCR
    /// service is set up. CV_READ_WITH_MODEL=0 turns it off. The image goes to the model as it is (it cannot be
    /// redacted like text), so the model must be the ministry's own or in-country.
    /// </summary>
    public async Task<string?> ReadImagesAsync(IReadOnlyList<ImageInput> images, CancellationToken ct = default)
    {
        if (!IsConfigured || images.Count == 0 || cfg["CV_READ_WITH_MODEL"] == "0") return null;
        var model = cfg["LLM_VISION_MODEL"] is { Length: > 0 } v ? v : ModelName;
        var content = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = CvImagePrompt } };
        foreach (var img in images)
            content.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = $"data:{img.MediaType};base64,{Convert.ToBase64String(img.Data)}" },
            });
        var body = Body([new JsonObject { ["role"] = "user", ["content"] = content }], 0, model);
        var data = await PostAsync(body, ct);
        await RecordAsync(data, "cv_read_image", model);
        var text = Strip(data["choices"]?[0]?["message"]?["content"]?.GetValue<string>()) ?? "";
        return text.Trim().Length == 0 ? null : text;
    }

    const string CvImagePrompt = """
        These images are the pages of one CV (a scan or a photo). Write out all of its text in reading order, exactly as it
        is written and in its own language (Arabic, English or both): headings, names of employers and institutions,
        dates, job titles, skills and lists, each on its own line. Do not translate, summarise, correct or add anything,
        and do not describe the layout or the images. If a page has no readable text, write nothing for it.
        """;

    async Task RecordAsync(JsonNode data, string feature, string modelName)
    {
        if (usageLog == null) return;
        var usage = data["usage"];
        int Tokens(string k) { try { return usage?[k]?.GetValue<int>() ?? 0; } catch (Exception e) when (e is InvalidOperationException or FormatException) { return 0; } }
        await usageLog.RecordAsync("api", feature, modelName, Tokens("prompt_tokens"), Tokens("completion_tokens"));
    }

    /// <summary>
    /// The longest answer asked for (LLM_MAX_TOKENS, default 8000). Without it some services reserve credit for the
    /// model's whole output limit (over 100,000 tokens) on every call and refuse the call when credit is low.
    /// </summary>
    static readonly int MaxTokens = int.TryParse(Environment.GetEnvironmentVariable("LLM_MAX_TOKENS"), out var m) && m > 0 ? m : 8000;

    JsonObject Body(JsonNode[] messages, double temperature, string? model = null)
    {
        var body = new JsonObject { ["model"] = model ?? ModelName, ["messages"] = new JsonArray(messages), ["temperature"] = temperature, ["max_tokens"] = MaxTokens };
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
