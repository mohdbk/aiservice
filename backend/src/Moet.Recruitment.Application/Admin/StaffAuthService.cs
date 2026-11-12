using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Identity;

namespace Moet.Recruitment.Application.Admin;

public sealed record StaffSession(int Id, string Email, string FullName, StaffRole Role, int SessionVersion);

/// <summary>Staff sign-in for the admin panel, and managing staff accounts (administrators only).</summary>
public sealed class StaffAuthService(IAppDbContext db, IPasswordHasher hasher, IClock clock)
{
    public async Task<StaffSession> SignInAsync(string? email, string? password, CancellationToken ct = default)
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

    public static void Validate(string? password)
    {
        if ((password ?? "").Length is < 12 or > 128) throw DomainException.Invalid("password_short", new Dictionary<string, object?> { ["min"] = 12 });
    }

    // ---------------- staff accounts (administrators) ----------------

    public async Task<object> ListAsync(CancellationToken ct = default) =>
        new
        {
            staff = await db.StaffUsers.AsNoTracking().OrderBy(u => u.FullName)
                .Select(u => new { u.Id, u.Email, u.FullName, role = u.Role, u.IsEnabled, u.LastSignInAt }).ToListAsync(ct),
        };

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
