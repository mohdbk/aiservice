using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Assessments;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Assessments;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Portal;

/// <summary>A candidate's tests on the career portal.</summary>
public sealed class PortalAssessmentService(
    IAppDbContext db, ContentService content, PortalAccountService accounts, AssessmentWorkflow workflow, IClock clock, IVirusScanner scanner)
{
    static readonly Dictionary<string, (string Type, byte[] Signature)> AnswerFiles = new()
    {
        ["pdf"] = ("application/pdf", [0x25, 0x50, 0x44, 0x46]),
        ["docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", [0x50, 0x4B, 0x03, 0x04]),
        ["xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [0x50, 0x4B, 0x03, 0x04]),
        ["pptx"] = ("application/vnd.openxmlformats-officedocument.presentationml.presentation", [0x50, 0x4B, 0x03, 0x04]),
        ["png"] = ("image/png", [0x89, 0x50, 0x4E, 0x47]),
        ["jpg"] = ("image/jpeg", [0xFF, 0xD8, 0xFF]),
        ["jpeg"] = ("image/jpeg", [0xFF, 0xD8, 0xFF]),
        ["zip"] = ("application/zip", [0x50, 0x4B, 0x03, 0x04]),
    };

    public async Task<object> ListAsync(string? email, string? epoch, string? lang, CancellationToken ct = default)
    {
        lang = await content.RequireLanguageAsync(lang, ct);
        var user = await accounts.RequireAsync(email, epoch, ct);
        var codes = await db.Invitations.AsNoTracking()
            .Where(i => i.JobApplication!.ApplicantAccountId == user.Id && i.Status != InvitationStatus.Cancelled)
            .OrderByDescending(i => i.CreatedAt).Take(100).Select(i => i.Code).ToListAsync(ct);
        var rows = new List<object>();
        foreach (var code in codes)
            rows.Add(await workflow.CandidateSummaryAsync(await workflow.LoadForCandidateAsync(user.Id, code, ct), lang, ct));
        return new { assessments = rows };
    }

    public async Task<object> DetailAsync(string? email, string? epoch, string? code, string? lang, CancellationToken ct = default)
    {
        lang = await content.RequireLanguageAsync(lang, ct);
        var user = await accounts.RequireAsync(email, epoch, ct);
        return await workflow.CandidateSummaryAsync(await workflow.LoadForCandidateAsync(user.Id, code, ct), lang, ct);
    }

    public async Task<object> StartAsync(string? email, string? epoch, string? code, string? lang, CancellationToken ct = default)
    {
        lang = await content.RequireLanguageAsync(lang, ct);
        var user = await accounts.RequireAsync(email, epoch, ct);
        var inv = await workflow.LoadForCandidateAsync(user.Id, code, ct);
        var a = await workflow.StartAsync(inv, ct);
        if (inv.LanguageCode != lang)
        {
            inv.LanguageCode = lang;
            await db.SaveChangesAsync(ct);
        }
        return workflow.State(a, lang);
    }

    public async Task<object> StateAsync(string? email, string? epoch, string? code, string? lang, CancellationToken ct = default)
    {
        lang = await content.RequireLanguageAsync(lang, ct);
        var user = await accounts.RequireAsync(email, epoch, ct);
        var inv = await workflow.LoadForCandidateAsync(user.Id, code, ct);
        var a = await workflow.OpenAttemptAsync(inv, ct);
        // Questions come in the language the candidate chose when starting (they can change it from the start page).
        return a == null ? new { finished = true } : workflow.State(a, inv.LanguageCode);
    }

    async Task<(Invitation, Attempt)> CurrentAsync(string? email, string? epoch, string? code, CancellationToken ct)
    {
        var user = await accounts.RequireAsync(email, epoch, ct);
        var inv = await workflow.LoadForCandidateAsync(user.Id, code, ct);
        var a = await workflow.OpenAttemptAsync(inv, ct) ?? throw DomainException.Conflict("section_closed");
        return (inv, a);
    }

    public async Task<object> SaveAnswerAsync(string? email, string? epoch, string? code, string? key, string? answer, int section, CancellationToken ct = default)
    {
        var (inv, a) = await CurrentAsync(email, epoch, code, ct);
        await workflow.SaveAnswerAsync(inv, a, section, key ?? "", AssessmentWorkflow.ParseAnswer(answer), ct);
        return new { saved = true };
    }

    public async Task<object> UploadAsync(string? email, string? epoch, string? code, string? key, int section, string? fileName, byte[]? data, CancellationToken ct = default)
    {
        var (inv, a) = await CurrentAsync(email, epoch, code, ct);
        if (section != a.CurrentSectionNo) throw DomainException.Conflict("section_closed");
        if (data == null || data.Length == 0) throw DomainException.Invalid("file_required");
        var ext = Path.GetExtension(fileName ?? "").TrimStart('.').ToLowerInvariant();
        if (!AnswerFiles.TryGetValue(ext, out var kind) || data.Length < kind.Signature.Length || !data.AsSpan(0, kind.Signature.Length).SequenceEqual(kind.Signature))
            throw DomainException.Invalid("file_type");
        if (data.Length > 10 * 1024 * 1024) throw DomainException.Invalid("file_too_large", new Dictionary<string, object?> { ["max_mb"] = 10 });
        await scanner.CheckAsync(data, ct);
        var name = TextUtil.Clip(Path.GetFileName(fileName), 200);
        var file = new StoredFile
        {
            Id = Guid.NewGuid(), FileName = $"{inv.Code}-{a.AttemptNo}-{key}.{ext}", ContentType = kind.Type, SizeBytes = data.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), Content = data, CreatedAt = clock.UtcNow,
        };
        db.StoredFiles.Add(file);
        await workflow.SaveAnswerAsync(inv, a, section, key ?? "", new AnswerInput(null, null, file.Id, name), ct);
        return new { saved = true, name };
    }

    public async Task<object> NextSectionAsync(string? email, string? epoch, string? code, string? lang, int section, CancellationToken ct = default)
    {
        lang = await content.RequireLanguageAsync(lang, ct);
        var (inv, a) = await CurrentAsync(email, epoch, code, ct);
        var next = await workflow.NextSectionAsync(inv, a, section, ct);
        return next == null ? new { finished = true } : workflow.State(next, inv.LanguageCode);
    }

    public async Task<object> EventAsync(string? email, string? epoch, string? code, string? kind, CancellationToken ct = default)
    {
        try
        {
            var (_, a) = await CurrentAsync(email, epoch, code, ct);
            await workflow.LogEventAsync(a, kind ?? "", ct);
        }
        catch (DomainException) { }
        return new { ok = true };
    }
}
