using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Backoffice;

public sealed record DocumentTemplateInput(string? Kind, string? NameAr, string? NameEn, string? BodyAr, string? BodyEn, bool IsDefault, bool IsActive,
    string? RowVersion, string? BodyHtmlAr = null, string? BodyHtmlEn = null);

/// <summary>A document made from a template for one record: plain text and HTML, in Arabic or English.</summary>
public sealed record RenderedDocument(int TemplateId, string TemplateName, string Lang, string Text, string Html);

/// <summary>
/// Document templates (recruitment admins) of every kind, and the documents made from them: offer letters for offers,
/// interview letters for interviews, letters for applicants.
/// </summary>
public sealed class DocumentService(IAppDbContext db, ICurrentActor actor, IClock clock, PortalMailer mailer, ContentService content, IHtmlCleaner cleaner,
    IPdfRenderer pdf)
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<object> ListAsync(string? kind, bool? active, CancellationToken ct = default)
    {
        var q = db.DocumentTemplates.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(kind)) q = q.Where(t => t.Kind == kind);
        if (active != null) q = q.Where(t => t.IsActive == active);
        var rows = await q.OrderBy(t => t.Kind).ThenByDescending(t => t.IsDefault).ThenBy(t => t.NameEn).ToListAsync(ct);
        return new
        {
            items = rows.Select(Full),
            kinds = DocumentKinds.All.Select(k => new { key = k.Key, name_ar = k.Value.NameAr, name_en = k.Value.NameEn, placeholders = k.Value.Placeholders }),
        };
    }

    public async Task<object> GetAsync(int id, CancellationToken ct = default) =>
        Full(await db.DocumentTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound());

    static object Full(DocumentTemplate t) => new
    {
        id = t.Id, kind = t.Kind, name_ar = t.NameAr, name_en = t.NameEn, body_ar = t.BodyAr, body_en = t.BodyEn, body_html_ar = t.BodyHtmlAr,
        body_html_en = t.BodyHtmlEn, is_default = t.IsDefault, is_active = t.IsActive, updated_at = t.UpdatedAt, row_version = Convert.ToBase64String(t.RowVersion),
    };

    public async Task<object> SaveAsync(int? id, DocumentTemplateInput input, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        DocumentTemplate t;
        if (id == null)
        {
            t = new DocumentTemplate { CreatedAt = now, Kind = input.Kind ?? "" };
            db.DocumentTemplates.Add(t);
        }
        else
        {
            t = await db.DocumentTemplates.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
            if (!string.IsNullOrEmpty(input.RowVersion) && input.RowVersion != Convert.ToBase64String(t.RowVersion))
                throw DomainException.Conflict("changed_elsewhere");
        }
        t.NameAr = TextUtil.NoEmDash(TextUtil.Clip(input.NameAr?.Trim(), 150));
        t.NameEn = TextUtil.NoEmDash(TextUtil.Clip(input.NameEn?.Trim(), 150));
        t.BodyAr = TextUtil.NoEmDash(TextUtil.Clip(input.BodyAr?.Trim(), 50000));
        t.BodyEn = TextUtil.NoEmDash(TextUtil.Clip(input.BodyEn?.Trim(), 50000));
        t.BodyHtmlAr = CleanHtml(input.BodyHtmlAr);
        t.BodyHtmlEn = CleanHtml(input.BodyHtmlEn);
        t.IsActive = input.IsActive;
        t.IsDefault = input.IsDefault;
        t.UpdatedAt = now;
        t.UpdatedByStaffId = actor.Id;
        t.Validate();
        // One default per kind.
        if (t.IsDefault)
            foreach (var other in await db.DocumentTemplates.Where(x => x.Kind == t.Kind && x.IsDefault && x.Id != t.Id).ToListAsync(ct)) other.IsDefault = false;
        await db.SaveChangesAsync(ct);
        return new { id = t.Id };
    }

    string CleanHtml(string? html)
    {
        var clean = TextUtil.NoEmDash(cleaner.Clean(TextUtil.Clip(html, 200000)));
        return TextUtil.StripHtml(clean).Trim().Length == 0 ? "" : clean;
    }

    public async Task<object> DeleteAsync(int id, CancellationToken ct = default)
    {
        var t = await db.DocumentTemplates.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        if (await db.JobOffers.AnyAsync(o => o.OfferTemplateId == id, ct)) throw DomainException.Conflict("template_in_use");
        db.DocumentTemplates.Remove(t);
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    // ---------------- documents ----------------

    /// <summary>The values a kind's placeholders take for one record, and the record's language and chosen template.</summary>
    async Task<(Dictionary<string, string> Values, string AppLang, int? TemplateId)> DataAsync(string kind, int recordId, string lang, CancellationToken ct)
    {
        JobApplication app;
        var values = new Dictionary<string, string>();
        int? templateId = null;
        if (kind == DocumentKinds.OfferLetter)
        {
            var o = await db.JobOffers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == recordId, ct) ?? throw DomainException.NotFound();
            app = await LoadApplicantAsync(o.JobApplicationId, ct);
            templateId = o.OfferTemplateId;
            values["position_title"] = o.PositionTitle;
            values["offer_date"] = o.OfferDate.ToString("dd/MM/yyyy", Inv);
            values["start_date"] = o.StartDate?.ToString("dd/MM/yyyy", Inv) ?? "";
            values["monthly_salary"] = o.MonthlySalary?.ToString("#,0.##", Inv) ?? "";
            values["currency"] = o.Currency;
            values["terms"] = o.Terms.Trim();
            values["offer_code"] = o.Code;
        }
        else if (kind == DocumentKinds.InterviewLetter)
        {
            var i = await db.Interviews.AsNoTracking().Include(x => x.InterviewRound).FirstOrDefaultAsync(x => x.Id == recordId, ct) ?? throw DomainException.NotFound();
            app = await LoadApplicantAsync(i.JobApplicationId, ct);
            var start = clock.Local(i.StartsAt);
            var end = clock.Local(i.EndsAt);
            values["round"] = lang == "en" && i.InterviewRound!.NameEn.Length > 0 ? i.InterviewRound.NameEn : i.InterviewRound!.Name;
            values["date"] = start.ToString("dd/MM/yyyy", Inv);
            values["time"] = $"{start:HH\\:mm} - {end:HH\\:mm}";
            values["location"] = i.Location;
            values["interview_code"] = i.Code;
        }
        else if (kind == DocumentKinds.ApplicantLetter) app = await LoadApplicantAsync(recordId, ct);
        else throw DomainException.Invalid("invalid_value");
        var job = app.JobOpening!;
        values["name"] = app.FullName;
        values["reference"] = app.Reference;
        values["job_title"] = job.In(lang).Title;
        values["department"] = job.Department?.NameIn(lang) ?? "";
        values["applied_on"] = clock.Local(app.CreatedAt).ToString("dd/MM/yyyy", Inv);
        values["today"] = clock.Today().ToString("dd/MM/yyyy", Inv);
        return (values, app.LanguageCode, templateId);
    }

    async Task<JobApplication> LoadApplicantAsync(int id, CancellationToken ct) =>
        await db.JobApplications.AsNoTracking().Include(a => a.JobOpening!).ThenInclude(j => j.Translations)
            .Include(a => a.JobOpening!).ThenInclude(j => j.Department!).ThenInclude(d => d.Translations)
            .FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw DomainException.NotFound();

    /// <summary>
    /// Makes the document: the template (given, the one chosen on the record, or the kind's default) filled with the record's
    /// data, in the requested language or the candidate's. Null when the kind has no active template.
    /// </summary>
    public async Task<RenderedDocument?> RenderAsync(string kind, int recordId, int? templateId, string? lang, CancellationToken ct = default)
    {
        // The candidate's language unless another is asked for (the template exists in Arabic and English).
        var probe = await DataAsync(kind, recordId, "ar", ct);
        var l = lang is "ar" or "en" ? lang : probe.AppLang == "ar" ? "ar" : "en";
        var (values, _, chosen) = l == "ar" ? probe : await DataAsync(kind, recordId, l, ct);
        var id = templateId ?? chosen;
        var t = id is { } tid
            ? await db.DocumentTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tid && x.Kind == kind, ct)
            : await db.DocumentTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Kind == kind && x.IsDefault && x.IsActive, ct);
        if (t == null) return null;
        var text = EmailTemplates.Fill(t.In(l), values).Replace("\r", "");
        // Lines left empty by an unused placeholder (for example no terms) do not leave gaps.
        while (text.Contains("\n\n\n")) text = text.Replace("\n\n\n", "\n\n");
        text = text.Trim();
        var designed = t.HtmlIn(l);
        string inner;
        if (designed.Length > 0)
        {
            inner = designed;
            foreach (var (k, v) in values) inner = inner.Replace("{" + k + "}", TextUtil.Esc(v).Replace("\n", "<br>"));
        }
        else inner = PortalMailer.Paragraphs(text, "", "");
        return new RenderedDocument(t.Id, l == "ar" ? t.NameAr : t.NameEn, l, text, PortalMailer.Wrap(inner, "", l == "ar"));
    }

    public async Task<object> DocumentAsync(string kind, int recordId, int? templateId, string? lang, CancellationToken ct = default)
    {
        var d = await RenderAsync(kind, recordId, templateId, lang, ct);
        return d == null
            ? new { template_id = (int?)null, template = "", lang = "", text = "", html = "" }
            : new { template_id = (int?)d.TemplateId, template = d.TemplateName, lang = d.Lang, text = d.Text, html = d.Html };
    }

    /// <summary>The document as a PDF file (null when the kind has no active template).</summary>
    public async Task<(byte[] Content, string FileName)?> PdfAsync(string kind, int recordId, int? templateId, string? lang, CancellationToken ct = default)
    {
        var d = await RenderAsync(kind, recordId, templateId, lang, ct);
        if (d == null) return null;
        var settings = await content.SettingsAsync(ct);
        var logo = settings.LogoFileId is { } lid ? await db.StoredFiles.AsNoTracking().Where(f => f.Id == lid && f.ContentType != "image/svg+xml").Select(f => f.Content).FirstOrDefaultAsync(ct) : null;
        var bytes = pdf.Letter(d.TemplateName, d.Text, d.Lang == "ar", logo);
        var name = new string(d.TemplateName.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return (bytes, $"{(name.Length == 0 ? "letter" : name)}-{recordId}.pdf");
    }

    /// <summary>Emails the offer (email template "offer_sent") with the letter. False when the email is off or not set up.</summary>
    public async Task<bool> EmailOfferAsync(int offerId, CancellationToken ct = default)
    {
        var o = await db.JobOffers.AsNoTracking().FirstAsync(x => x.Id == offerId, ct);
        var app = await LoadApplicantAsync(o.JobApplicationId, ct);
        var lang = await content.PortalLanguageAsync(app.LanguageCode, ct);
        var l = lang == "ar" ? "ar" : "en";
        var letter = await RenderAsync(DocumentKinds.OfferLetter, offerId, null, l, ct);
        var hasDesign = letter != null && (await db.DocumentTemplates.AsNoTracking().Where(t => t.Id == letter.TemplateId)
            .Select(t => l == "ar" ? t.BodyHtmlAr : t.BodyHtmlEn).FirstAsync(ct)).Length > 0;
        // The letter also goes as a PDF file when the email settings say so.
        MailAttachment? file = null;
        if (letter != null && await db.EmailSettings.AsNoTracking().Select(s => (bool?)s.AttachOfferPdf).FirstOrDefaultAsync(ct) != false
            && await PdfAsync(DocumentKinds.OfferLetter, offerId, null, l, ct) is { } p)
            file = new MailAttachment(p.FileName, "application/pdf", p.Content);
        return await mailer.QueueAsync(app.Email, app.FullName, lang, "offer_sent", "", new Dictionary<string, string>
        {
            ["job_title"] = app.JobOpening!.In(l).Title, ["position_title"] = o.PositionTitle,
            ["start_date"] = o.StartDate?.ToString("dd/MM/yyyy", Inv) ?? "",
            ["offer_letter"] = letter?.Text ?? "",
            // The designed letter goes into the HTML part when the template has one.
            ["offer_letter_html"] = hasDesign ? letter!.Html : "",
        }, ct, file);
    }
}
