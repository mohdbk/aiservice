using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Hiring;
using Moet.Recruitment.Domain.Identity;
using Moet.Recruitment.Domain.Operations;
using Moet.Recruitment.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Moet.Recruitment.Api.Tests;

/// <summary>Runs only when LOAD_DB_CONNECTION is set (its own database, so the other tests' counts stay exact).</summary>
public sealed class LoadFactAttribute : FactAttribute
{
    public LoadFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LOAD_DB_CONNECTION"))) Skip = "Set LOAD_DB_CONNECTION to run the load test.";
    }
}

/// <summary>
/// One job with 6,000 applicants (CV scores and summaries, a third interviewed with panel ratings), then the pages and the
/// assistant's tools timed against it. The assistant's tools must stay small (a page of results plus totals), never the
/// whole list, and every call must answer within a few seconds.
/// </summary>
public class LoadTests(ITestOutputHelper output)
{
    static readonly string? Db = Environment.GetEnvironmentVariable("LOAD_DB_CONNECTION");
    const int Applicants = 6000;
    /// <summary>The agent truncates any tool result above this (RecruitmentTools.MaxResultChars).</summary>
    const int AgentMaxChars = 12000;

    sealed class App : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseSetting("DB_CONNECTION", Db);
            b.UseSetting("ADMIN_EMAIL", "admin@moet.test");
            b.UseSetting("ADMIN_PASSWORD", "Admin-Password-123");
            b.UseSetting("PORTAL_API_KEY", "load-portal-key");
            b.UseSetting("PORTAL_API_SECRET", "load-portal-secret-0123456789");
            b.UseSetting("COOKIE_SECURE", "0");
            b.UseSetting("RUN_WORKERS", "0");
            b.UseSetting("AGENT_KEY", "load-agent-key-0123456789");
        }
    }

    static StringContent JsonBody(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    static readonly string[] Skills = ["SQL", "Power BI", "Python", "economic modelling", "public policy", "tourism events", "Arabic writing", "project management", "statistics", "Excel"];

    [LoadFact]
    public async Task Six_thousand_applicants_on_one_job()
    {
        using var app = new App();
        var admin = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        admin.DefaultRequestHeaders.Add("X-Backoffice", "1");
        Assert.True((await admin.PostAsync("/api/backoffice/sign-in", JsonBody(new { email = "admin@moet.test", password = "Admin-Password-123" }))).IsSuccessStatusCode);
        var job = JsonDocument.Parse(await (await admin.PostAsync("/api/backoffice/jobs", JsonBody(new
        {
            title = "محلل بيانات اقتصادية", description = "<p>تحليل البيانات</p>", department_id = (int?)null, location = "دبي", employment_type = "FullTime",
            status = "Open", is_published = true, posted_on = (string?)null, closes_on = (string?)null, salary_min = (decimal?)null, salary_max = (decimal?)null,
            salary_currency = "AED", salary_period = "Month", show_salary = false, translations = Array.Empty<object>(), screening = Array.Empty<object>(),
            stages = Array.Empty<object>(), row_version = (string?)null,
        }))).Content.ReadAsStringAsync()).RootElement;
        var jobId = job.GetProperty("id").GetInt32();
        var round = JsonDocument.Parse(await (await admin.PostAsync("/api/backoffice/rounds", JsonBody(new { name = "مقابلة", name_en = "Interview", is_active = true, description = "", skills = new[] { "Analysis" } }))).Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        // Seed straight into the database (the API is the thing being measured).
        var seed = Stopwatch.StartNew();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var rnd = new Random(7);
            var now = DateTime.UtcNow;
            var file = new StoredFile { Id = Guid.NewGuid(), FileName = "cv.pdf", ContentType = "application/pdf", SizeBytes = 4, Sha256 = "x", Content = "%PDF"u8.ToArray(), CreatedAt = now };
            db.StoredFiles.Add(file);
            await db.SaveChangesAsync();
            for (var batch = 0; batch < Applicants / 500; batch++)
            {
                var accounts = Enumerable.Range(batch * 500, 500).Select(i => new ApplicantAccount
                {
                    Email = $"load{i}@example.test", FullName = $"Applicant {i}", PasswordHash = "", IsEmailVerified = true, CreatedAt = now.AddDays(-rnd.Next(0, 80)),
                    InTalentPool = i % 4 == 0, TalentPoolAt = i % 4 == 0 ? now : null,
                }).ToList();
                db.ApplicantAccounts.AddRange(accounts);
                await db.SaveChangesAsync();
                var apps = accounts.Select((a, k) =>
                {
                    var i = batch * 500 + k;
                    var skills = string.Join(", ", Enumerable.Range(0, 3).Select(_ => Skills[rnd.Next(Skills.Length)]));
                    return new JobApplication
                    {
                        Code = $"LD-{i:D6}", JobOpeningId = jobId, ApplicantAccountId = a.Id, FullName = a.FullName, Email = a.Email, Phone = "+971501234567",
                        Country = "United Arab Emirates", CoverNote = "", Status = i % 10 == 0 ? ApplicationStatus.Rejected : ApplicationStatus.Open,
                        LanguageCode = i % 3 == 0 ? "en" : "ar", DeclarationAcceptedAt = a.CreatedAt, CvFileId = file.Id, ScreeningOutcome = ScreeningOutcome.NoQuestions,
                        CreatedAt = a.CreatedAt, UpdatedAt = a.CreatedAt,
                        CvAnalysis = new CvAnalysis
                        {
                            Status = CvAnalysisStatus.Scored, Score = rnd.Next(30, 100), YearsOfExperience = rnd.Next(0, 20), UpdatedAt = now, ScoredAt = now,
                            Summary = $"Analyst with {rnd.Next(1, 15)} years in government and banking. Skills: {skills}. Led reporting for senior management and built dashboards.",
                        },
                    };
                }).ToList();
                db.JobApplications.AddRange(apps);
                await db.SaveChangesAsync();
                // A third were interviewed, with one panelist's ratings.
                var interviews = apps.Where((_, k) => k % 3 == 0).Select(x => new Interview
                {
                    Code = $"LI-{x.Code[3..]}", JobApplicationId = x.Id, InterviewRoundId = round, StartsAt = now.AddDays(-3), EndsAt = now.AddDays(-3).AddHours(1),
                    Status = InterviewStatus.UnderReview, CreatedAt = now, UpdatedAt = now,
                    Feedback = [new InterviewFeedback
                    {
                        StaffUserId = 1, Recommendation = Recommendation.Cleared, Comments = "Good", SubmittedAt = now,
                        Ratings = [new InterviewFeedbackRating { Skill = "Analysis", Rating = (byte)rnd.Next(1, 6) }],
                    }],
                }).ToList();
                db.Interviews.AddRange(interviews);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
            }
        }
        output.WriteLine($"Seeded {Applicants} applicants in {seed.Elapsed.TotalSeconds:0.0} s");

        // The pages, and what each assistant tool asks for (same query as RecruitmentTools).
        var calls = new (string Name, string Path, bool Agent)[]
        {
            ("Applicants list (page 1)", $"/api/backoffice/applicants?job={jobId}", false),
            ("Applicants list, last page", $"/api/backoffice/applicants?job={jobId}&page=240", false),
            ("search_applicants (best CV scores)", $"/api/backoffice/applicants?job={jobId}&sort=score&dir=desc&page=1&size=20", true),
            ("search_applicants (cv text: Power BI)", $"/api/backoffice/applicants?cv=Power%20BI&sort=score&dir=desc&page=1&size=20", true),
            ("rank_applicants (combined score)", $"/api/backoffice/jobs/{jobId}/ranking?page=1&size=25", true),
            ("rank_applicants, all incl. rejected", $"/api/backoffice/jobs/{jobId}/ranking?all=true&page=1&size=25", true),
            ("list_jobs", "/api/backoffice/jobs?page=1&size=20", true),
            ("get_hiring_report (job)", $"/api/backoffice/reports/hiring?job={jobId}", true),
            ("get_hiring_report (all)", "/api/backoffice/reports/hiring", true),
            ("get_dashboard", "/api/backoffice/dashboard", true),
            ("search_talent_pool", "/api/backoffice/talent-pool?page=1&size=20", true),
            ("list_interviews", "/api/backoffice/interviews?scope=all&size=20", true),
            ("Suggestions", "/api/backoffice/agent/suggestions", true),
            ("Audit log", "/api/backoffice/audit?size=25", false),
        };
        var report = new StringBuilder("\n| Call | ms | chars | agent sees |\n|---|---|---|---|\n");
        var problems = new List<string>();
        foreach (var (name, path, agent) in calls)
        {
            await admin.GetAsync(path);   // warm up (query plans)
            var sw = Stopwatch.StartNew();
            var res = await admin.GetAsync(path);
            var body = await res.Content.ReadAsStringAsync();
            sw.Stop();
            Assert.True(res.StatusCode == HttpStatusCode.OK, $"{name}: {(int)res.StatusCode} {body}");
            var sees = !agent ? "-" : body.Length <= AgentMaxChars ? "whole result" : "TRUNCATED";
            report.Append($"| {name} | {sw.ElapsedMilliseconds} | {body.Length} | {sees} |\n");
            if (sw.ElapsedMilliseconds > 4000) problems.Add($"{name} took {sw.ElapsedMilliseconds} ms");
            if (agent && body.Length > AgentMaxChars) problems.Add($"{name} returns {body.Length} chars, above what the assistant reads ({AgentMaxChars})");
            if (path.Contains("ranking") || path.Contains("applicants?job"))
            {
                using var doc = JsonDocument.Parse(body);
                Assert.True(doc.RootElement.GetProperty("total").GetInt32() >= Applicants * 9 / 10, $"{name}: total");
            }
        }
        output.WriteLine(report.ToString());
        Assert.True(problems.Count == 0, string.Join("\n", problems) + report);
    }
}
