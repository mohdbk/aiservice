using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Identity;

namespace Moet.Recruitment.Application.Portal;

public sealed record ApplicantSession(string Email, string FullName, int Epoch);

/// <summary>Applicant accounts: sign-up, sign-in, email verification and password reset.</summary>
public sealed class PortalAccountService(IAppDbContext db, ContentService content, PortalMailer mailer, IPasswordHasher hasher, IClock clock)
{
    static readonly Dictionary<TokenPurpose, (string Page, string Subject, string Body)> Mail = new()
    {
        [TokenPurpose.VerifyEmail] = ("verify-email", "email.verify_subject", "email.verify_body"),
        [TokenPurpose.ResetPassword] = ("reset-password", "email.reset_subject", "email.reset_body"),
    };

    async Task<bool> VerificationRequiredAsync(CancellationToken ct) => (await content.SettingsAsync(ct)).RequireEmailVerification && mailer.CanSend;

    public async Task<object> SignUpAsync(string? fullName, string? email, string? password, string? lang, string? nextPath, CancellationToken ct = default)
    {
        var s = await content.SettingsAsync(ct);
        if (!s.AllowSignup) throw DomainException.Forbidden("signup_closed");
        fullName = (fullName ?? "").Trim();
        email = TextUtil.NormEmail(email);
        if (fullName.Length is < 2 or > 140) throw DomainException.Invalid("name_required");
        if (!TextUtil.ValidEmail(email)) throw DomainException.Invalid("email_invalid");
        if ((password ?? "").Length is < 8 or > 128) throw DomainException.Invalid("password_short");
        if (!StrongEnough(password!)) throw DomainException.Invalid("password_weak");
        if (await db.ApplicantAccounts.AnyAsync(a => a.Email == email, ct)) throw DomainException.Conflict("account_exists");
        lang = await content.PortalLanguageAsync(lang, ct);
        var verify = await VerificationRequiredAsync(ct);
        var account = new ApplicantAccount
        {
            Email = email, FullName = fullName, PasswordHash = hasher.Hash(password!), IsEmailVerified = !verify,
            PreferredLanguageCode = lang, CreatedAt = clock.UtcNow,
        };
        db.ApplicantAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        if (verify)
        {
            await SendLinkAsync(account, TokenPurpose.VerifyEmail, lang, nextPath, ct);
            await db.SaveChangesAsync(ct);
            return new { email, full_name = fullName, verification_sent = true };
        }
        return new ApplicantSession(email, fullName, 0);
    }

    /// <summary>Common passwords and passwords made of one repeated character are refused.</summary>
    internal static bool StrongEnough(string p)
    {
        var lower = p.ToLowerInvariant();
        string[] common = ["password", "12345678", "123456789", "qwertyui", "11111111", "password1", "iloveyou", "00000000", "abcd1234", "moet1234"];
        return p.Distinct().Count() >= 4 && !common.Contains(lower);
    }

    public async Task<ApplicantSession> AuthenticateAsync(string? email, string? password, CancellationToken ct = default)
    {
        email = TextUtil.NormEmail(email);
        var now = clock.UtcNow;
        var a = email.Length == 0 ? null : await db.ApplicantAccounts.FirstOrDefaultAsync(x => x.Email == email, ct);
        if (a == null || !a.IsEnabled)
        {
            hasher.Verify("$dummy", password ?? "");
            throw new DomainException("bad_credentials", 401);
        }
        if (SignInLockout.IsLocked(a.LockedUntil, now)) throw new DomainException("too_many_attempts", 429);
        if (!hasher.Verify(a.PasswordHash, password ?? ""))
        {
            (a.FailedSignIns, a.LockedUntil) = SignInLockout.Failed(a.FailedSignIns, now);
            await db.SaveChangesAsync(ct);
            throw new DomainException("bad_credentials", 401);
        }
        if (!a.IsEmailVerified && await VerificationRequiredAsync(ct)) throw DomainException.Forbidden("email_not_verified");
        a.FailedSignIns = 0;
        a.LockedUntil = null;
        a.LastSignInAt = now;
        await db.SaveChangesAsync(ct);
        return new ApplicantSession(a.Email, a.FullName, a.SessionVersion);
    }

    /// <summary>The signed-in applicant. A password reset raises the session version, which signs out old sessions.</summary>
    public async Task<ApplicantAccount> RequireAsync(string? email, string? epoch, CancellationToken ct = default)
    {
        email = TextUtil.NormEmail(email);
        var a = email.Length == 0 ? null : await db.ApplicantAccounts.FirstOrDefaultAsync(x => x.Email == email, ct);
        if (a == null || !a.IsEnabled || (epoch ?? "0") != a.SessionVersion.ToString()) throw new DomainException("signed_out", 401);
        return a;
    }

    /// <summary>Always answers the same way, so it cannot reveal which emails have accounts.</summary>
    public async Task<object> ResendVerificationAsync(string? email, string? lang, string? nextPath, CancellationToken ct = default)
    {
        email = TextUtil.NormEmail(email);
        var a = await db.ApplicantAccounts.FirstOrDefaultAsync(x => x.Email == email, ct);
        if (a is { IsEnabled: true, IsEmailVerified: false } && await VerificationRequiredAsync(ct))
        {
            await SendLinkAsync(a, TokenPurpose.VerifyEmail, await content.PortalLanguageAsync(lang, ct), nextPath, ct);
            await db.SaveChangesAsync(ct);
        }
        return new { sent = true };
    }

    public async Task<object> VerifyEmailAsync(string? token, CancellationToken ct = default)
    {
        var t = await ConsumeAsync(token, TokenPurpose.VerifyEmail, ct);
        t.ApplicantAccount!.IsEmailVerified = true;
        await db.SaveChangesAsync(ct);
        var a = t.ApplicantAccount;
        return new { email = a.Email, full_name = a.FullName, epoch = a.SessionVersion, next = t.NextPath, lang = t.LanguageCode };
    }

    /// <summary>Always answers the same way, so it cannot reveal which emails have accounts.</summary>
    public async Task<object> RequestPasswordResetAsync(string? email, string? lang, CancellationToken ct = default)
    {
        email = TextUtil.NormEmail(email);
        var a = await db.ApplicantAccounts.FirstOrDefaultAsync(x => x.Email == email, ct);
        if (a is { IsEnabled: true })
        {
            await SendLinkAsync(a, TokenPurpose.ResetPassword, await content.PortalLanguageAsync(lang, ct), "", ct);
            await db.SaveChangesAsync(ct);
        }
        return new { sent = true };
    }

    public async Task<object> ResetPasswordAsync(string? token, string? password, CancellationToken ct = default)
    {
        if ((password ?? "").Length is < 8 or > 128) throw DomainException.Invalid("password_short");
        if (!StrongEnough(password!)) throw DomainException.Invalid("password_weak");
        var t = await ConsumeAsync(token, TokenPurpose.ResetPassword, ct);
        var a = t.ApplicantAccount!;
        a.PasswordHash = hasher.Hash(password!);
        a.PasswordChanged();
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    async Task<AccountToken> ConsumeAsync(string? token, TokenPurpose purpose, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 200) throw DomainException.Invalid("token_invalid");
        var hash = TextUtil.Sha256Hex(token);
        var t = await db.AccountTokens.Include(x => x.ApplicantAccount).FirstOrDefaultAsync(x => x.TokenHash == hash && x.Purpose == purpose, ct)
                ?? throw DomainException.Invalid("token_invalid");
        t.Consume(clock.UtcNow);
        return t;
    }

    /// <summary>Issues a new single-use link (older unused links for the same purpose stop working) and queues the email.</summary>
    async Task SendLinkAsync(ApplicantAccount a, TokenPurpose purpose, string lang, string? nextPath, CancellationToken ct)
    {
        var (page, subject, body) = Mail[purpose];
        if ((await mailer.LinkAsync(lang, "", ct)).Length == 0 || !mailer.CanSend) return;
        var now = clock.UtcNow;
        await db.AccountTokens.Where(x => x.ApplicantAccountId == a.Id && x.Purpose == purpose && x.UsedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.UsedAt, now), ct);
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        db.AccountTokens.Add(new AccountToken
        {
            ApplicantAccountId = a.Id, Purpose = purpose, TokenHash = TextUtil.Sha256Hex(token), LanguageCode = lang,
            NextPath = TextUtil.SafeNext(nextPath), ExpiresAt = now + AccountToken.Lifetime(purpose), CreatedAt = now,
        });
        await mailer.QueueAsync(a.Email, a.FullName, lang, subject, body, await mailer.LinkAsync(lang, $"/{page}?token={token}", ct), ct: ct);
    }

    static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async Task PurgeOldTokensAsync(CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow.AddDays(-7);
        await db.AccountTokens.Where(t => t.ExpiresAt < cutoff).ExecuteDeleteAsync(ct);
    }
}
