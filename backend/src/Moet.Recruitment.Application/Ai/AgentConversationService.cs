using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Ai;

public sealed record ConversationInput(string? Title, JsonElement Content);

/// <summary>
/// Assistant conversations on the server (each staff member's own), shareable read-only, and the suggestions the assistant
/// offers from what needs attention today.
/// </summary>
public sealed class AgentConversationService(IAppDbContext db, ICurrentActor actor, IClock clock)
{
    int Me => actor.Id ?? throw DomainException.Forbidden();

    static bool ValidThread(string? t) => t is { Length: >= 8 and <= 64 } && t.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public async Task<object> ListAsync(ListQuery lq, CancellationToken ct = default)
    {
        var me = Me;
        var q = db.AgentConversations.AsNoTracking().Where(c => c.StaffUserId == me);
        if (lq.Term.Length > 0) q = q.Where(c => c.Title.Contains(lq.Term));
        return await q.PageAsync(lq, new Sorts<AgentConversation>("updated").Add("updated", c => c.UpdatedAt).Add("title", c => c.Title),
            c => new { thread = c.Thread, title = c.Title, shared = c.IsShared, updated_at = c.UpdatedAt }, ct);
    }

    /// <summary>Mine, or one a colleague shared. Whose it is comes back so the page can open a shared one as a copy.</summary>
    public async Task<object> GetAsync(string thread, CancellationToken ct = default)
    {
        var me = Me;
        var c = await db.AgentConversations.AsNoTracking().FirstOrDefaultAsync(x => x.Thread == thread && (x.StaffUserId == me || x.IsShared), ct)
            ?? throw DomainException.NotFound();
        var owner = c.StaffUserId == me ? "" : await db.StaffUsers.Where(u => u.Id == c.StaffUserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "";
        return new { thread = c.Thread, title = c.Title, shared = c.IsShared, mine = c.StaffUserId == me, owner, updated_at = c.UpdatedAt, content = JsonDocument.Parse(c.Content).RootElement };
    }

    public async Task<object> SaveAsync(string thread, ConversationInput input, CancellationToken ct = default)
    {
        if (!ValidThread(thread)) throw DomainException.Invalid("invalid_value");
        var me = Me;
        var raw = input.Content.ValueKind == JsonValueKind.Object ? input.Content.GetRawText() : throw DomainException.Invalid("invalid_value");
        if (raw.Length > AgentConversation.MaxContent) throw DomainException.Invalid("conversation_too_long");
        var c = await db.AgentConversations.FirstOrDefaultAsync(x => x.Thread == thread, ct);
        if (c == null) db.AgentConversations.Add(c = new AgentConversation { Thread = thread, StaffUserId = me, CreatedAt = clock.UtcNow });
        else if (c.StaffUserId != me) throw DomainException.Forbidden();
        c.Title = TextUtil.NoEmDash(TextUtil.Clip((input.Title ?? "").Trim().ReplaceLineEndings(" "), 200));
        c.Content = raw;
        c.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    public async Task<object> ShareAsync(string thread, bool shared, CancellationToken ct = default)
    {
        var me = Me;
        var c = await db.AgentConversations.FirstOrDefaultAsync(x => x.Thread == thread && x.StaffUserId == me, ct) ?? throw DomainException.NotFound();
        c.IsShared = shared;
        await db.SaveChangesAsync(ct);
        return new { shared = c.IsShared };
    }

    public async Task<object> DeleteAsync(string thread, CancellationToken ct = default)
    {
        var me = Me;
        await db.AgentConversations.Where(x => x.Thread == thread && x.StaffUserId == me).ExecuteDeleteAsync(ct);
        return new { ok = true };
    }

    /// <summary>
    /// What needs attention now, as suggestions the staff member can hand to the assistant with one tap. Only those with
    /// something to do are returned, most pressing first.
    /// </summary>
    public async Task<object> SuggestionsAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var today = clock.Today();
        var dayStart = clock.ToUtc(today, TimeOnly.MinValue);
        var list = new List<(int Count, string Key, string Ar, string En, string AskAr, string AskEn)>();
        var toReview = await db.JobApplications.CountAsync(a => a.Status == ApplicationStatus.Open && a.AnonymisedAt == null && a.CreatedAt < now.AddDays(-3), ct);
        list.Add((toReview, "to_review", $"{toReview} طلبات تنتظر المراجعة منذ أكثر من 3 أيام", $"{toReview} applications waiting over 3 days for review",
            "اعرض الطلبات المفتوحة منذ أكثر من 3 أيام مرتبة حسب التقييم", "Show the applications open for more than 3 days, best scores first"));
        var toMark = await db.Attempts.CountAsync(a => a.Status == AttemptStatus.Submitted, ct);
        list.Add((toMark, "to_mark", $"{toMark} اختبارات بانتظار التصحيح", $"{toMark} tests waiting to be marked",
            "ما الاختبارات التي تنتظر التصحيح؟", "Which tests are waiting to be marked?"));
        var noFeedback = await db.Interviews.CountAsync(i => i.EndsAt < now && i.Status == InterviewStatus.Scheduled, ct);
        list.Add((noFeedback, "no_feedback", $"{noFeedback} مقابلات انتهت دون تقييم من اللجنة", $"{noFeedback} interviews ended without panel feedback",
            "اعرض المقابلات المنتهية التي لم تقيمها اللجنة بعد", "Show past interviews that have no panel feedback yet"));
        var todayInterviews = await db.Interviews.CountAsync(i => i.StartsAt >= dayStart && i.StartsAt < dayStart.AddDays(1) && i.Status == InterviewStatus.Scheduled, ct);
        list.Add((todayInterviews, "today", $"{todayInterviews} مقابلات اليوم", $"{todayInterviews} interviews today",
            "ما مقابلات اليوم؟ لخص كل مرشح", "What interviews are on today? Summarise each candidate"));
        var silentOffers = await db.JobOffers.CountAsync(o => o.Status == OfferStatus.Sent && o.UpdatedAt < now.AddDays(-7), ct);
        list.Add((silentOffers, "offers", $"{silentOffers} عروض أرسلت منذ أكثر من أسبوع دون رد", $"{silentOffers} offers sent over a week ago with no reply",
            "اعرض العروض المرسلة منذ أكثر من أسبوع دون رد", "Show offers sent more than a week ago with no reply"));
        var closing = await db.JobOpenings.CountAsync(j => j.Status == JobStatus.Open && j.ClosesOn != null && j.ClosesOn >= today && j.ClosesOn <= today.AddDays(3), ct);
        list.Add((closing, "closing", $"{closing} وظائف تغلق خلال 3 أيام", $"{closing} jobs close within 3 days",
            "ما الوظائف التي تغلق خلال 3 أيام وكم عدد المتقدمين لكل منها؟", "Which jobs close within 3 days, and how many applicants does each have?"));
        var english = await db.JobOpeningTranslations.CountAsync(t => t.ReviewStatus == ReviewStatus.Suggested, ct);
        list.Add((english, "english", $"{english} نصوص إنجليزية مقترحة تنتظر المراجعة", $"{english} suggested English texts waiting for review",
            "ما الوظائف التي لها نص إنجليزي مقترح ينتظر المراجعة؟", "Which jobs have a suggested English text waiting for review?"));
        return list.Where(x => x.Count > 0).Take(5).Select(x => new { key = x.Key, count = x.Count, text_ar = x.Ar, text_en = x.En, ask_ar = x.AskAr, ask_en = x.AskEn });
    }
}
