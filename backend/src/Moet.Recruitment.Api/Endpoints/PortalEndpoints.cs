using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moet.Recruitment.Application.Portal;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Infrastructure;

namespace Moet.Recruitment.Api.Endpoints;

/// <summary>
/// The API the career portal backend (in the DMZ) calls: /api/portal/{method}. Authenticated with the portal
/// service key ("Authorization: token KEY:SECRET"); browsers never reach it. The portal backend has already
/// signed the applicant in and passes their email and session version. Replies are {"message": ...}; failures are
/// {"message": {"error": {"code", "params"}}} with a 4xx status, so the portal shows them in the visitor's language.
/// </summary>
public static class PortalEndpoints
{
    sealed record Input(Dictionary<string, string> Values, IFormFile? File)
    {
        public string? this[string k] => Values.TryGetValue(k, out var v) ? v : null;
        public int Int(string k, int d = -1) => int.TryParse(this[k], out var n) ? n : d;
    }

    public static void Map(WebApplication app)
    {
        app.MapMethods("/api/portal/{method}", ["GET", "POST"], Handle).DisableAntiforgery();
    }

    static async Task<IResult> Handle(string method, HttpContext ctx, IConfiguration cfg, IServiceProvider sp, CancellationToken ct)
    {
        if (!Authorised(ctx, cfg)) return Results.Json(Envelope.Error("forbidden", null), Json.Options, statusCode: 403);
        var input = await ReadAsync(ctx, ct);
        if (input["email"] is { Length: > 0 } email) ctx.Items[HttpCurrentActor.ApplicantHeader] = email;
        var site = sp.GetRequiredService<PortalSiteService>();
        var acc = sp.GetRequiredService<PortalAccountService>();
        var apps = sp.GetRequiredService<PortalApplicationService>();
        var asm = sp.GetRequiredService<PortalAssessmentService>();
        var post = HttpMethods.IsPost(ctx.Request.Method);
        try
        {
            if (method == "logo")
            {
                var (content, type, name) = await site.LogoAsync(input["dark"] == "1", ct);
                return Results.File(content, type, name);
            }
            object result = (method, post) switch
            {
                ("config", false) => await site.ConfigAsync(input["lang"], ct),
                ("jobs", false) => await site.JobsAsync(input["lang"], input["q"], input["department"], input["location"], input.Int("page", 1), ct),
                ("job", false) => await site.JobAsync(input["lang"], input["name"], ct),
                ("signup", true) => await acc.SignUpAsync(input["full_name"], input["email"], input["password"], input["lang"], input["next_path"], ct),
                ("authenticate", true) => await acc.AuthenticateAsync(input["email"], input["password"], ct),
                ("resend_verification", true) => await acc.ResendVerificationAsync(input["email"], input["lang"], input["next_path"], ct),
                ("verify_email", true) => await acc.VerifyEmailAsync(input["token"], ct),
                ("request_password_reset", true) => await acc.RequestPasswordResetAsync(input["email"], input["lang"], ct),
                ("reset_password", true) => await acc.ResetPasswordAsync(input["token"], input["password"], ct),
                ("applications", false) => await apps.ListAsync(input["email"], input["epoch"], input["lang"], ct),
                ("application", false) => await apps.DetailAsync(input["email"], input["epoch"], input["name"], input["lang"], ct),
                ("add_correction", true) => await apps.AddCorrectionAsync(input["email"], input["epoch"], input["name"], input["note"], ct),
                ("apply", true) => await apps.ApplyAsync(new ApplyInput(input["email"], input["epoch"], input["job"], input["phone"],
                    input["country"], input["cover_note"], input["declaration"], input["lang"], input["screening"],
                    input.File?.FileName, await BytesAsync(input.File, ct)), ct),
                ("assessments", false) => await asm.ListAsync(input["email"], input["epoch"], input["lang"], ct),
                ("assessment", false) => await asm.DetailAsync(input["email"], input["epoch"], input["name"], input["lang"], ct),
                ("start_assessment", true) => await asm.StartAsync(input["email"], input["epoch"], input["name"], input["lang"], ct),
                ("assessment_state", false) => await asm.StateAsync(input["email"], input["epoch"], input["name"], input["lang"], ct),
                ("save_assessment_answer", true) => await asm.SaveAnswerAsync(input["email"], input["epoch"], input["name"], input["key"],
                    input["answer"], input.Int("section"), ct),
                ("upload_assessment_file", true) => await asm.UploadAsync(input["email"], input["epoch"], input["name"], input["key"],
                    input.Int("section"), input.File?.FileName, await BytesAsync(input.File, ct), ct),
                ("next_assessment_section", true) => await asm.NextSectionAsync(input["email"], input["epoch"], input["name"], input["lang"],
                    input.Int("section"), ct),
                ("assessment_event", true) => await asm.EventAsync(input["email"], input["epoch"], input["name"], input["kind"], ct),
                _ => throw DomainException.NotFound("not_found"),
            };
            return Results.Json(new { message = result }, Json.Options);
        }
        catch (DomainException e)
        {
            return Results.Json(Envelope.Error(e.Code, e.Params), Json.Options, statusCode: e.Status);
        }
    }

    static bool Authorised(HttpContext ctx, IConfiguration cfg)
    {
        var key = cfg["PORTAL_API_KEY"] ?? "";
        var secret = cfg["PORTAL_API_SECRET"] ?? "";
        if (key.Length < 8 || secret.Length < 16) return false;
        var header = ctx.Request.Headers.Authorization.ToString();
        var expected = $"token {key}:{secret}";
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header), Encoding.UTF8.GetBytes(expected));
    }

    static async Task<Input> ReadAsync(HttpContext ctx, CancellationToken ct)
    {
        var values = new Dictionary<string, string>();
        foreach (var (k, v) in ctx.Request.Query) values[k] = v.ToString();
        IFormFile? file = null;
        if (ctx.Request.HasFormContentType)
        {
            var form = await ctx.Request.ReadFormAsync(ct);
            foreach (var (k, v) in form) values[k] = v.ToString();
            file = form.Files.GetFile("cv") ?? form.Files.GetFile("file");
        }
        else if (ctx.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ct);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var p in doc.RootElement.EnumerateObject())
                    values[p.Name] = p.Value.ValueKind switch
                    {
                        JsonValueKind.String => p.Value.GetString() ?? "",
                        JsonValueKind.Null => "",
                        _ => p.Value.GetRawText(),
                    };
        }
        return new Input(values, file);
    }

    static async Task<byte[]?> BytesAsync(IFormFile? f, CancellationToken ct)
    {
        if (f == null) return null;
        using var ms = new MemoryStream();
        await f.CopyToAsync(ms, ct);
        return ms.ToArray();
    }
}
