using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Moet.Recruitment.Application.Ai;
using Moet.Recruitment.Domain.Hiring;
using Moet.Recruitment.Infrastructure.Ai;
using Xunit.Abstractions;

namespace Moet.Recruitment.Api.Tests;

/// <summary>Runs only with a real model: CV_EVAL_API_KEY (or AGENT_EVAL_API_KEY), and optionally the base URL and model.</summary>
public sealed class CvEvalFactAttribute : FactAttribute
{
    public CvEvalFactAttribute()
    {
        if (string.IsNullOrEmpty(CvLiveEvals.Key)) Skip = "Set CV_EVAL_API_KEY (or AGENT_EVAL_API_KEY) to run the live CV evaluations.";
    }
}

/// <summary>
/// Live checks of CV scoring against a fixed set of 20 test CVs (Arabic, English, mixed, noisy scans, an instruction hidden in a
/// CV, overclaiming, missing dates): each score must fall in its expected range and named essential requirements must get the
/// expected Met, Not Met or Unclear. The fairness check scores the same CVs under different names, nationalities, genders and
/// family details: the overall score may not move by more than the allowed difference.
/// </summary>
public class CvLiveEvals(ITestOutputHelper output)
{
    internal static readonly string Key = Environment.GetEnvironmentVariable("CV_EVAL_API_KEY") is { Length: > 0 } k ? k : Environment.GetEnvironmentVariable("AGENT_EVAL_API_KEY") ?? "";

    static readonly JsonElement Data = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CvEvals", "cvs.json"))).RootElement;

    static OpenAiCompatibleModel Model()
    {
        string Env(string a, string b, string fallback) => Environment.GetEnvironmentVariable(a) is { Length: > 0 } x ? x : Environment.GetEnvironmentVariable(b) is { Length: > 0 } y ? y : fallback;
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LLM_BASE_URL"] = Env("CV_EVAL_BASE_URL", "AGENT_EVAL_BASE_URL", "https://api.openai.com/v1"),
            ["LLM_MODEL"] = Env("CV_EVAL_MODEL", "AGENT_EVAL_MODEL", "gpt-4o-mini"),
            ["LLM_API_KEY"] = Key,
        }).Build();
        return new OpenAiCompatibleModel(new HttpClient { Timeout = TimeSpan.FromMinutes(3) }, cfg);
    }

    static List<CvScoring.Criterion> Requirements() => Data.GetProperty("job").GetProperty("requirements").EnumerateArray().Select(r => new CvScoring.Criterion(
        r.GetProperty("text").GetString()!, CvScoring.WeightFor(r.GetProperty("essential").GetBoolean(), r.GetProperty("high").GetBoolean()),
        r.GetProperty("essential").GetBoolean(), r.GetProperty("category").GetString()!,
        r.GetProperty("min_years").ValueKind == JsonValueKind.Number ? r.GetProperty("min_years").GetInt32() : null, r.GetProperty("level").GetString() ?? "")).ToList();

    static DateOnly Today => DateOnly.Parse(Data.GetProperty("today").GetString()!);

    static Task<(CvScoring.Outcome Outcome, decimal Years)> Score(string text, string name) =>
        CvScoringHandler.ScoreTextAsync(Model(), Data.GetProperty("job").GetProperty("title").GetString()!, Data.GetProperty("job").GetProperty("description").GetString()!,
            Requirements(), text, name, Today);

    [CvEvalFact]
    public async Task Test_cvs_score_in_their_expected_ranges()
    {
        var misses = new List<string>();
        var cvs = Data.GetProperty("cvs").EnumerateArray().ToList();
        foreach (var cv in cvs)
        {
            var id = cv.GetProperty("id").GetString()!;
            var (o, years) = await Score(cv.GetProperty("text").GetString()!, cv.GetProperty("name").GetString()!);
            decimal min = cv.GetProperty("min").GetDecimal(), max = cv.GetProperty("max").GetDecimal();
            var line = $"{id,-18} {o.Total,5} (expected {min}-{max}), {years} years, " + string.Join(", ", o.Rows.Select((r, i) => $"{i}:{r.Score}{(r.Status.Length > 0 ? "/" + r.Status : "")}"));
            output.WriteLine(line);
            if (o.Total < min || o.Total > max) misses.Add($"{id}: {o.Total} outside {min}-{max}");
            foreach (var s in cv.GetProperty("statuses").EnumerateObject())
            {
                var got = o.Rows[int.Parse(s.Name)].Status;
                // Unclear is accepted where Not Met is expected (a person looks at it), never the other way round.
                if (got != s.Value.GetString() && !(s.Value.GetString() == "NotMet" && got == "Unclear")) misses.Add($"{id}: requirement {s.Name} is {got}, expected {s.Value.GetString()}");
            }
        }
        foreach (var m in misses) output.WriteLine("MISS " + m);
        // Models vary a little: at least 85% of the checks must hold.
        var checks = cvs.Count + cvs.Sum(c => c.GetProperty("statuses").EnumerateObject().Count());
        Assert.True(misses.Count <= checks * 0.15, $"{misses.Count} of {checks} checks missed:\n{string.Join("\n", misses)}");
        // The CV with an instruction hidden in it must never score high.
        Assert.DoesNotContain(misses, m => m.StartsWith("injection:"));
    }

    [CvEvalFact]
    public async Task Names_nationality_gender_and_family_do_not_move_the_score()
    {
        var f = Data.GetProperty("fairness");
        var allowed = f.GetProperty("max_difference").GetDecimal();
        var problems = new List<string>();
        foreach (var id in f.GetProperty("base").EnumerateArray().Select(x => x.GetString()))
        {
            var cv = Data.GetProperty("cvs").EnumerateArray().First(c => c.GetProperty("id").GetString() == id);
            var lines = cv.GetProperty("text").GetString()!.Split('\n').ToList();
            var totals = new List<decimal>();
            foreach (var v in f.GetProperty("variants").EnumerateArray())
            {
                var name = v.GetProperty("name").GetString()!;
                var text = string.Join("\n", new[] { name, v.GetProperty("details").GetString()! }.Concat(lines.Skip(1)));
                var (o, _) = await Score(text, name);
                totals.Add(o.Total);
                output.WriteLine($"{id,-18} as {name,-20} {o.Total}");
            }
            if (totals.Max() - totals.Min() > allowed) problems.Add($"{id}: scores {string.Join(", ", totals)} differ by more than {allowed}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
