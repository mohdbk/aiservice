using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Backoffice;

public sealed record OfferTemplateInput(string? NameAr, string? NameEn, string? BodyAr, string? BodyEn, bool IsDefault, bool IsActive, string? RowVersion);

/// <summary>Offer letter templates (recruitment admins), and the letters made from them for each offer.</summary>
public sealed class OfferLetterService(IAppDbContext db, ICurrentActor actor, IClock clock, PortalMailer mailer, ContentService content)
{
    public async Task<object> ListAsync(bool? active, CancellationToken ct = default)
    {
        var q = db.OfferTemplates.AsNoTracking().AsQueryable();
        if (active != null) q = q.Where(t => t.IsActive == active);
        var rows = await q.OrderByDescending(t => t.IsDefault).ThenBy(t => t.NameEn).ToListAsync(ct);
        return new { items = rows.Select(Full), placeholders = OfferTemplate.Placeholders };
    }

    public async Task<object> GetAsync(int id, CancellationToken ct = default) =>
        Full(await db.OfferTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound());

    static object Full(OfferTemplate t) => new
    {
        id = t.Id, name_ar = t.NameAr, name_en = t.NameEn, body_ar = t.BodyAr, body_en = t.BodyEn, is_default = t.IsDefault, is_active = t.IsActive,
        updated_at = t.UpdatedAt, row_version = Convert.ToBase64String(t.RowVersion),
    };

    public async Task<object> SaveAsync(int? id, OfferTemplateInput input, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        OfferTemplate t;
        if (id == null)
        {
            t = new OfferTemplate { CreatedAt = now };
            db.OfferTemplates.Add(t);
        }
        else
        {
            t = await db.OfferTemplates.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
            if (!string.IsNullOrEmpty(input.RowVersion) && input.RowVersion != Convert.ToBase64String(t.RowVersion))
                throw DomainException.Conflict("changed_elsewhere");
        }
        t.NameAr = TextUtil.NoEmDash(TextUtil.Clip(input.NameAr?.Trim(), 150));
        t.NameEn = TextUtil.NoEmDash(TextUtil.Clip(input.NameEn?.Trim(), 150));
        t.BodyAr = TextUtil.NoEmDash(TextUtil.Clip(input.BodyAr?.Trim(), 50000));
        t.BodyEn = TextUtil.NoEmDash(TextUtil.Clip(input.BodyEn?.Trim(), 50000));
        t.IsActive = input.IsActive;
        t.IsDefault = input.IsDefault;
        t.UpdatedAt = now;
        t.UpdatedByStaffId = actor.Id;
        t.Validate();
        // One default at a time.
        if (t.IsDefault)
            foreach (var other in await db.OfferTemplates.Where(x => x.IsDefault && x.Id != t.Id).ToListAsync(ct)) other.IsDefault = false;
        await db.SaveChangesAsync(ct);
        return new { id = t.Id };
    }

    public async Task<object> DeleteAsync(int id, CancellationToken ct = default)
    {
        var t = await db.OfferTemplates.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        if (await db.JobOffers.AnyAsync(o => o.OfferTemplateId == id, ct)) throw DomainException.Conflict("template_in_use");
        db.OfferTemplates.Remove(t);
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    async Task<(JobOffer Offer, JobApplication App, OfferTemplate? Template)> LoadAsync(int offerId, CancellationToken ct)
    {
        var o = await db.JobOffers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == offerId, ct) ?? throw DomainException.NotFound();
        var app = await db.JobApplications.AsNoTracking().Include(a => a.JobOpening!).ThenInclude(j => j.Translations)
            .Include(a => a.JobOpening!).ThenInclude(j => j.Department!).ThenInclude(d => d.Translations)
            .FirstAsync(a => a.Id == o.JobApplicationId, ct);
        var t = o.OfferTemplateId is { } tid
            ? await db.OfferTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tid, ct)
            : await db.OfferTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.IsDefault && x.IsActive, ct);
        return (o, app, t);
    }

    /// <summary>The offer's letter as text (paragraphs separated by a blank line, "# " headings), in Arabic or English.</summary>
    public static string Render(OfferTemplate t, JobOffer o, JobApplication app, string lang)
    {
        var job = app.JobOpening!;
        var culture = CultureInfo.InvariantCulture;
        var values = new Dictionary<string, string>
        {
            ["name"] = app.FullName, ["reference"] = app.Reference, ["offer_code"] = o.Code, ["job_title"] = job.In(lang).Title,
            ["department"] = job.Department?.NameIn(lang) ?? "", ["position_title"] = o.PositionTitle,
            ["offer_date"] = o.OfferDate.ToString("dd/MM/yyyy", culture), ["start_date"] = o.StartDate?.ToString("dd/MM/yyyy", culture) ?? "",
            ["monthly_salary"] = o.MonthlySalary?.ToString("#,0.##", culture) ?? "", ["currency"] = o.Currency, ["terms"] = o.Terms.Trim(),
        };
        var text = EmailTemplates.Fill(t.In(lang), values).Replace("\r", "");
        // Lines left empty by an unused placeholder (for example no terms) do not leave gaps.
        while (text.Contains("\n\n\n")) text = text.Replace("\n\n\n", "\n\n");
        return text.Trim();
    }

    /// <summary>The letter for the offer page (preview and print), in the candidate's language unless another is asked for.</summary>
    public async Task<object> LetterAsync(int offerId, string? lang, CancellationToken ct = default)
    {
        var (o, app, t) = await LoadAsync(offerId, ct);
        if (t == null) return new { template_id = (int?)null, lang = "", text = "", html = "" };
        var l = lang is "ar" or "en" ? lang : app.LanguageCode == "ar" ? "ar" : "en";
        var text = Render(t, o, app, l);
        return new { template_id = (int?)t.Id, template = l == "ar" ? t.NameAr : t.NameEn, lang = l, text, html = PortalMailer.Html(text, "", "", "", l == "ar") };
    }

    /// <summary>Emails the offer (template "offer_sent") with the letter in the body. False when the email is off or not set up.</summary>
    public async Task<bool> EmailOfferAsync(int offerId, CancellationToken ct = default)
    {
        var (o, app, t) = await LoadAsync(offerId, ct);
        var lang = await content.PortalLanguageAsync(app.LanguageCode, ct);
        var l = lang == "ar" ? "ar" : "en";
        return await mailer.QueueAsync(app.Email, app.FullName, lang, "offer_sent", "", new Dictionary<string, string>
        {
            ["job_title"] = app.JobOpening!.In(l).Title, ["position_title"] = o.PositionTitle,
            ["start_date"] = o.StartDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
            ["offer_letter"] = t == null ? "" : Render(t, o, app, l),
        }, ct);
    }
}
