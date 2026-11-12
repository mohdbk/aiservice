using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Domain.Identity;

public class StaffUser
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public StaffRole Role { get; set; } = StaffRole.Recruiter;
    public bool IsEnabled { get; set; } = true;
    public int SessionVersion { get; set; }
    public int FailedSignIns { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? LastSignInAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public bool CanAdminister => Role is StaffRole.RecruitmentAdmin or StaffRole.Administrator;
}

public class ApplicantAccount
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsEmailVerified { get; set; }
    public int SessionVersion { get; set; }
    public string? PreferredLanguageCode { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int FailedSignIns { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? LastSignInAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>A password reset signs the applicant out of every session.</summary>
    public void PasswordChanged()
    {
        SessionVersion++;
        IsEmailVerified = true;
        FailedSignIns = 0;
        LockedUntil = null;
    }
}

/// <summary>Single-use link for email verification or password reset. Only the SHA-256 of the token is stored.</summary>
public class AccountToken
{
    public static readonly TimeSpan VerifyLifetime = TimeSpan.FromHours(48);
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

    public long Id { get; set; }
    public int ApplicantAccountId { get; set; }
    public ApplicantAccount? ApplicantAccount { get; set; }
    public TokenPurpose Purpose { get; set; }
    public string TokenHash { get; set; } = "";
    public string LanguageCode { get; set; } = "ar";
    public string NextPath { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Marks the link used, or throws token_invalid / token_expired.</summary>
    public void Consume(DateTime now)
    {
        if (UsedAt != null) throw DomainException.Invalid("token_invalid");
        if (ExpiresAt < now) throw DomainException.Invalid("token_expired");
        UsedAt = now;
    }

    public static TimeSpan Lifetime(TokenPurpose p) => p == TokenPurpose.VerifyEmail ? VerifyLifetime : ResetLifetime;
}

/// <summary>Sign-in lockout: five wrong passwords lock the account for 15 minutes.</summary>
public static class SignInLockout
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(15);

    public static bool IsLocked(DateTime? lockedUntil, DateTime now) => lockedUntil != null && lockedUntil > now;

    public static (int Failures, DateTime? LockedUntil) Failed(int failures, DateTime now) =>
        failures + 1 >= MaxFailures ? (0, now + Duration) : (failures + 1, null);
}
