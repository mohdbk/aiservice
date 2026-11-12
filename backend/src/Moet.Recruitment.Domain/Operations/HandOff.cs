using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Domain.Operations;

/// <summary>
/// Passing a hire to the HR system (HRMS) when the candidate accepts the offer: the details are POSTed as JSON to the HR
/// system's address, signed with a shared secret (HMAC SHA-256) so it can check they came from here. One row; the secret is
/// stored encrypted. The same details can be downloaded from the offer or exported as CSV.
/// </summary>
public class HandOffSetting
{
    public byte Id { get; set; } = 1;
    public bool IsEnabled { get; set; }
    public string Url { get; set; } = "";
    public string SecretProtected { get; set; } = "";
    /// <summary>Whether the CV file is included (base64) in what is sent.</summary>
    public bool IncludeCv { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedByStaffId { get; set; }

    public void Validate()
    {
        if (!IsEnabled && Url.Length == 0) return;
        if (!(Uri.TryCreate(Url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Host == "localhost")))
            throw DomainException.Invalid("handoff_url_invalid");
    }
}
