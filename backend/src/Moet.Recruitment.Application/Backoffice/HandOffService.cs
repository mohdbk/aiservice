using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Backoffice;

public sealed record HandOffInput(bool IsEnabled, string? Url, string? Secret, bool IncludeCv);

/// <summary>
/// Hands a hire to the HR system when the offer is accepted (a signed JSON POST, retried by the background worker), and
/// gives the same details as a download (JSON per offer, CSV of hires).
/// </summary>
public sealed class HandOffService(IAppDbContext db, ISecretProtector secrets, IClock clock, ICurrentActor actor, JobQueue jobs, IWebhookClient webhook)
    : IJobHandler
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public string JobType => JobTypes.HandOff;

    async Task<HandOffSetting> SettingAsync(CancellationToken ct) => await db.HandOffSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new HandOffSetting();

    public async Task<object> GetAsync(CancellationToken ct = default)
    {
        var s = await SettingAsync(ct);
        return new
        {
            is_enabled = s.IsEnabled, url = s.Url, has_secret = s.SecretProtected.Length > 0, include_cv = s.IncludeCv,
            waiting = await db.JobOffers.CountAsync(o => o.Status == OfferStatus.Accepted && o.HandedOffAt == null, ct),
            recent = await db.JobOffers.AsNoTracking().Where(o => o.Status == OfferStatus.Accepted).OrderByDescending(o => o.UpdatedAt).Take(10)
                .Select(o => new { id = o.Id, code = o.Code, name = o.JobApplication!.FullName, handed_off_at = o.HandedOffAt, error = o.HandOffError }).ToListAsync(ct),
        };
    }

    public async Task<object> SaveAsync(HandOffInput input, CancellationToken ct = default)
    {
        var s = await db.HandOffSettings.FirstOrDefaultAsync(ct);
        if (s == null) db.HandOffSettings.Add(s = new HandOffSetting());
        s.IsEnabled = input.IsEnabled;
        s.Url = TextUtil.Clip(input.Url?.Trim(), 500);
        if (!string.IsNullOrEmpty(input.Secret)) s.SecretProtected = secrets.Protect(input.Secret.Trim());
        s.IncludeCv = input.IncludeCv;
        s.Validate();
        if (s.IsEnabled && s.SecretProtected.Length == 0) throw DomainException.Invalid("handoff_secret_required");
        s.UpdatedAt = clock.UtcNow;
        s.UpdatedByStaffId = actor.Id;
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    /// <summary>Queues the hand-off of an accepted offer (when it is switched on). Called when the offer becomes Accepted.</summary>
    public async Task<bool> QueueAsync(int offerId, CancellationToken ct = default)
    {
        if (!(await SettingAsync(ct)).IsEnabled) return false;
        await jobs.EnqueueAsync(JobTypes.HandOff, offerId, ct);
        return true;
    }

    /// <summary>Sends again now (from the offer page); the result is reported straight away.</summary>
    public async Task<object> SendNowAsync(int offerId, CancellationToken ct = default)
    {
        var s = await SettingAsync(ct);
        if (!s.IsEnabled) throw DomainException.Conflict("handoff_off");
        try
        {
            await RunAsync(offerId, ct);
            return new { ok = true, error = "" };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return new { ok = false, error = TextUtil.Clip(e.Message, 300) };
        }
    }

    /// <summary>The background job: POSTs the hire, signed, and records the outcome on the offer. Throws so the job is retried.</summary>
    public async Task RunAsync(int offerId, CancellationToken ct)
    {
        var s = await SettingAsync(ct);
        if (!s.IsEnabled) return;
        var payload = await PayloadAsync(offerId, s.IncludeCv, ct);
        var body = JsonSerializer.Serialize(payload, Json);
        var secret = secrets.Unprotect(s.SecretProtected) ?? throw new InvalidOperationException("The hand-off secret cannot be read.");
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var offer = await db.JobOffers.FirstAsync(o => o.Id == offerId, ct);
        try
        {
            await webhook.PostAsync(s.Url, body, new Dictionary<string, string>
            {
                ["X-MoET-Event"] = "offer.accepted", ["X-MoET-Signature"] = "sha256=" + signature, ["X-MoET-Delivery"] = $"{offer.Code}-{clock.UtcNow.Ticks}",
            }, ct);
            offer.HandedOffAt = clock.UtcNow;
            offer.HandOffError = "";
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            offer.HandOffError = TextUtil.Clip(e.Message, 1000);
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    /// <summary>What the HR system receives for a hire.</summary>
    public async Task<object> PayloadAsync(int offerId, bool includeCv, CancellationToken ct = default)
    {
        var o = await db.JobOffers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == offerId, ct) ?? throw DomainException.NotFound();
        var a = await db.JobApplications.AsNoTracking().Include(x => x.JobOpening!).ThenInclude(j => j.Translations)
            .Include(x => x.JobOpening!).ThenInclude(j => j.Department!).ThenInclude(d => d.Translations)
            .FirstAsync(x => x.Id == o.JobApplicationId, ct);
        var job = a.JobOpening!;
        object? cv = null;
        if (includeCv)
        {
            var f = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == a.CvFileId, ct);
            if (f is { SizeBytes: > 0 }) cv = new { file_name = f.FileName, content_type = f.ContentType, base64 = Convert.ToBase64String(f.Content) };
        }
        return new
        {
            @event = "offer.accepted", sent_at = clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", Inv),
            candidate = new { full_name = a.FullName, email = a.Email, phone = a.Phone, country = a.Country, language = a.LanguageCode, reference = a.Reference },
            job = new
            {
                code = job.Code, title_ar = job.In("ar").Title, title_en = job.In("en").Title,
                department_ar = job.Department?.NameIn("ar") ?? "", department_en = job.Department?.NameIn("en") ?? "",
                employment_type = job.EmploymentType.ToString(), location = job.Location,
            },
            offer = new
            {
                code = o.Code, position_title = o.PositionTitle, offer_date = o.OfferDate.ToString("yyyy-MM-dd", Inv),
                start_date = o.StartDate?.ToString("yyyy-MM-dd", Inv), monthly_salary = o.MonthlySalary, currency = o.Currency,
                accepted_at = o.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", Inv),
            },
            cv,
        };
    }

    /// <summary>The JSON a person can download from the offer (without the CV file).</summary>
    public async Task<string> PayloadJsonAsync(int offerId, CancellationToken ct = default) =>
        JsonSerializer.Serialize(await PayloadAsync(offerId, false, ct), new JsonSerializerOptions(Json) { WriteIndented = true });

    /// <summary>Accepted offers in a period as CSV (UTF-8 with BOM so Excel shows Arabic), for HR systems that import files.</summary>
    public async Task<byte[]> HiresCsvAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var start = (from ?? clock.Today().AddYears(-1)).ToDateTime(TimeOnly.MinValue);
        var end = (to ?? clock.Today()).AddDays(1).ToDateTime(TimeOnly.MinValue);
        var rows = await db.JobOffers.AsNoTracking().Where(o => o.Status == OfferStatus.Accepted && o.UpdatedAt >= start && o.UpdatedAt < end)
            .OrderBy(o => o.UpdatedAt).Select(o => new
            {
                o.Code, o.PositionTitle, o.StartDate, o.MonthlySalary, o.Currency, o.UpdatedAt, o.HandedOffAt,
                a = new { o.JobApplication!.FullName, o.JobApplication.Email, o.JobApplication.Phone, o.JobApplication.Country, o.JobApplication.Code },
                Job = o.JobApplication.JobOpening!.Code, JobTitle = o.JobApplication.JobOpening.Title,
            }).ToListAsync(ct);
        static string C(string? v)
        {
            v ??= "";
            // Cells starting with a formula character are written as text (CSV injection).
            if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v;
            return v.Contains(',') || v.Contains('"') || v.Contains('\n') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }
        var sb = new StringBuilder("offer_code,application_code,full_name,email,phone,country,job_code,job_title,position_title,start_date,monthly_salary,currency,accepted_at,handed_off_at\r\n");
        foreach (var r in rows)
            sb.Append(string.Join(",", C(r.Code), C(r.a.Code), C(r.a.FullName), C(r.a.Email), C(r.a.Phone), C(r.a.Country), C(r.Job), C(r.JobTitle), C(r.PositionTitle),
                C(r.StartDate?.ToString("yyyy-MM-dd", Inv)), C(r.MonthlySalary?.ToString("0.##", Inv)), C(r.Currency), C(r.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", Inv)),
                C(r.HandedOffAt?.ToString("yyyy-MM-ddTHH:mm:ssZ", Inv)))).Append("\r\n");
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }
}
