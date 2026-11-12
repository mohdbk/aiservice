namespace Moet.Recruitment.Domain.Common;

/// <summary>
/// A rule was broken. Carries a stable code (the portal and the admin panel turn it into text in the user's
/// language), the HTTP status the API should answer with, and parameters for the message.
/// </summary>
public sealed class DomainException : Exception
{
    public string Code { get; }
    public int Status { get; }
    public IReadOnlyDictionary<string, object?> Params { get; }

    public DomainException(string code, int status = 400, IDictionary<string, object?>? parameters = null) : base(code)
    {
        Code = code;
        Status = status;
        Params = new Dictionary<string, object?>(parameters ?? new Dictionary<string, object?>());
    }

    public static DomainException NotFound(string code = "not_found") => new(code, 404);
    public static DomainException Forbidden(string code = "forbidden") => new(code, 403);
    public static DomainException Conflict(string code) => new(code, 409);
    public static DomainException Invalid(string code, IDictionary<string, object?>? p = null) => new(code, 400, p);
}
