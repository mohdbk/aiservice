using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Identity;

namespace Moet.Recruitment.Application.Backoffice;

public sealed record StaffSession(int Id, string Email, string FullName, StaffRole Role, int SessionVersion);

/// <summary>Staff sign-in for the backoffice, and managing staff accounts (administrators only).</summary>
public sealed class StaffAuthService(IAppDbContext db, IPasswordHasher hasher, IClock clock, ISecretProtector secrets)
{
    public async Task<StaffSession> SignInAsync(string? email, string? password, CancellationToken ct = default) => await SignInAsync(email, password, null, ct);

    /// <summary>
    /// Signs in with the password and, for someone who turned on two-step sign-in, the authenticator code. Without a code the
    /// answer is "code_required" (the password was right); a wrong code counts as a failed attempt.
    /// </summary>
    public async Task<StaffSession> SignInAsync(string? email, string? password, string? code, CancellationToken ct = default)
    {
        email = TextUtil.NormEmail(email);
        var now = clock.UtcNow;
        var u = email.Length == 0 ? null : await db.StaffUsers.FirstOrDefaultAsync(x => x.Email == email, ct);
        if (u == null || !u.IsEnabled)
        {
            hasher.Verify("$dummy", password ?? "");
            throw new DomainException("bad_credentials", 401);
        }
        if (SignInLockout.IsLocked(u.LockedUntil, now)) throw new DomainException("too_many_attempts", 429);
        if (!hasher.Verify(u.PasswordHash, password ?? ""))
        {
            (u.FailedSignIns, u.LockedUntil) = SignInLockout.Failed(u.FailedSignIns, now);
            await db.SaveChangesAsync(ct);
            throw new DomainException("bad_credentials", 401);
        }
        if (u.TotpEnabledAt != null)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new DomainException("code_required", 401);
            var secret = Secret(u.TotpSecretProtected) ?? throw DomainException.Conflict("two_step_unavailable");
            if (Totp.Verify(secret, code, now, u.TotpLastStep) is not { } step)
            {
                (u.FailedSignIns, u.LockedUntil) = SignInLockout.Failed(u.FailedSignIns, now);
                await db.SaveChangesAsync(ct);
                throw new DomainException("bad_code", 401);
            }
            u.TotpLastStep = step;
        }
        u.FailedSignIns = 0;
        u.LockedUntil = null;
        u.LastSignInAt = now;
        await db.SaveChangesAsync(ct);
        return new StaffSession(u.Id, u.Email, u.FullName, u.Role, u.SessionVersion);
    }

    /// <summary>The session is still valid: account enabled and not signed out everywhere since.</summary>
    public async Task<StaffSession?> ValidateAsync(int id, int sessionVersion, CancellationToken ct = default)
    {
        var u = await db.StaffUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return u is { IsEnabled: true } && u.SessionVersion == sessionVersion ? new StaffSession(u.Id, u.Email, u.FullName, u.Role, u.SessionVersion) : null;
    }

    public async Task ChangePasswordAsync(int id, string? current, string? next, CancellationToken ct = default)
    {
        var u = await db.StaffUsers.FirstAsync(x => x.Id == id, ct);
        if (!hasher.Verify(u.PasswordHash, current ?? "")) throw DomainException.Invalid("bad_current_password");
        Validate(next);
        u.PasswordHash = hasher.Hash(next!);
        u.SessionVersion++;
        u.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    byte[]? Secret(string protectedValue) => protectedValue.Length == 0 ? null : secrets.Unprotect(protectedValue) is { } b32 ? Totp.FromBase32(b32) : null;

    // ---------------- two-step sign-in (each person for themselves) ----------------

    public async Task<object> TwoStepAsync(int id, CancellationToken ct = default)
    {
        var u = await db.StaffUsers.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        return new { enabled = u.TotpEnabledAt != null, enabled_at = u.TotpEnabledAt };
    }

    /// <summary>A new secret to add to the authenticator app (kept aside until a code from the app confirms it).</summary>
    public async Task<object> SetUpTwoStepAsync(int id, CancellationToken ct = default)
    {
        if (!secrets.IsConfigured) throw DomainException.Conflict("two_step_unavailable");
        var u = await db.StaffUsers.FirstAsync(x => x.Id == id, ct);
        var secret = Totp.NewSecret();
        u.TotpPendingProtected = secrets.Protect(Totp.Base32(secret));
        await db.SaveChangesAsync(ct);
        return new { secret = Totp.Base32(secret), uri = Totp.Uri("MoET Recruitment", u.Email, secret) };
    }

    public async Task<object> EnableTwoStepAsync(int id, string? code, CancellationToken ct = default)
    {
        var u = await db.StaffUsers.FirstAsync(x => x.Id == id, ct);
        var secret = Secret(u.TotpPendingProtected) ?? throw DomainException.Conflict("two_step_not_set_up");
        if (Totp.Verify(secret, code, clock.UtcNow, 0) is not { } step) throw DomainException.Invalid("bad_code");
        u.TotpSecretProtected = u.TotpPendingProtected;
        u.TotpPendingProtected = "";
        u.TotpEnabledAt = clock.UtcNow;
        u.TotpLastStep = step;
        u.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return new { enabled = true, enabled_at = u.TotpEnabledAt };
    }

    /// <summary>Turning it off needs the password and a current code.</summary>
    public async Task<object> DisableTwoStepAsync(int id, string? password, string? code, CancellationToken ct = default)
    {
        var u = await db.StaffUsers.FirstAsync(x => x.Id == id, ct);
        if (!hasher.Verify(u.PasswordHash, password ?? "")) throw DomainException.Invalid("bad_current_password");
        if (u.TotpEnabledAt != null && (Secret(u.TotpSecretProtected) is not { } secret || Totp.Verify(secret, code, clock.UtcNow, u.TotpLastStep) == null))
            throw DomainException.Invalid("bad_code");
        ClearTwoStep(u);
        await db.SaveChangesAsync(ct);
        return new { enabled = false };
    }

    /// <summary>An administrator turns it off for someone who lost their phone (they sign in with the password and set it up again).</summary>
    public async Task<object> ResetTwoStepAsync(int id, CancellationToken ct = default)
    {
        var u = await db.StaffUsers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        ClearTwoStep(u);
        u.SessionVersion++;
        await db.SaveChangesAsync(ct);
        return new { enabled = false };
    }

    void ClearTwoStep(StaffUser u)
    {
        u.TotpSecretProtected = "";
        u.TotpPendingProtected = "";
        u.TotpEnabledAt = null;
        u.TotpLastStep = 0;
        u.UpdatedAt = clock.UtcNow;
    }

    public static void Validate(string? password)
    {
        if ((password ?? "").Length is < 12 or > 128) throw DomainException.Invalid("password_short", new Dictionary<string, object?> { ["min"] = 12 });
    }

    // ---------------- staff accounts (administrators) ----------------

    public Task<PageResult<object>> ListAsync(ListQuery lq, string? role, bool? enabled, CancellationToken ct = default)
    {
        var q = db.StaffUsers.AsNoTracking().AsQueryable();
        if (Enum.TryParse<StaffRole>(role, out var r)) q = q.Where(u => u.Role == r);
        if (enabled != null) q = q.Where(u => u.IsEnabled == enabled);
        if (lq.Term.Length > 0) q = q.Where(u => u.FullName.Contains(lq.Term) || u.Email.Contains(lq.Term));
        var sorts = new Sorts<StaffUser>("name", false).Add("name", u => u.FullName).Add("email", u => u.Email).Add("role", u => u.Role)
            .Add("last_sign_in", u => u.LastSignInAt);
        return q.PageAsync(lq, sorts, u => (object)new { id = u.Id, email = u.Email, full_name = u.FullName, role = u.Role, is_enabled = u.IsEnabled, last_sign_in_at = u.LastSignInAt, two_step = u.TotpEnabledAt != null }, ct);
    }

    public async Task<object> SaveAsync(int? id, string? email, string? fullName, string? role, bool enabled, string? password, CancellationToken ct = default)
    {
        email = TextUtil.NormEmail(email);
        if (!TextUtil.ValidEmail(email)) throw DomainException.Invalid("email_invalid");
        if (string.IsNullOrWhiteSpace(fullName)) throw DomainException.Invalid("name_required");
        if (!Enum.TryParse<StaffRole>(role, out var r)) throw DomainException.Invalid("role_invalid");
        var now = clock.UtcNow;
        StaffUser u;
        if (id == null)
        {
            if (await db.StaffUsers.AnyAsync(x => x.Email == email, ct)) throw DomainException.Conflict("account_exists");
            Validate(password);
            u = new StaffUser { CreatedAt = now, PasswordHash = hasher.Hash(password!) };
            db.StaffUsers.Add(u);
        }
        else
        {
            u = await db.StaffUsers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
            if (await db.StaffUsers.AnyAsync(x => x.Email == email && x.Id != id, ct)) throw DomainException.Conflict("account_exists");
            if (!string.IsNullOrEmpty(password))
            {
                Validate(password);
                u.PasswordHash = hasher.Hash(password);
                u.SessionVersion++;
            }
            if (u.IsEnabled && !enabled || u.Role != r) u.SessionVersion++;
        }
        u.Email = email;
        u.FullName = TextUtil.Clip(fullName.Trim(), 140);
        u.Role = r;
        u.IsEnabled = enabled;
        u.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return new { id = u.Id };
    }
}
