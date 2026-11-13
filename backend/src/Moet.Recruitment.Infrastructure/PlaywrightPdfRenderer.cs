using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Moet.Recruitment.Application.Common;

namespace Moet.Recruitment.Infrastructure;

/// <summary>
/// Letters as A4 PDFs printed by Chromium through Microsoft Playwright (Apache 2.0): the same HTML as the letter preview,
/// so Arabic joins and runs right to left exactly as in the browser. One browser is kept open and each letter gets its own
/// page, with scripts off and no network (only the letter itself and its inline logo are drawn). The browser lives in the
/// image (PLAYWRIGHT_BROWSERS_PATH); fonts come from the system (Noto Kufi Arabic, Noto Naskh Arabic, Noto Sans).
/// </summary>
public sealed class PlaywrightPdfRenderer(ILogger<PlaywrightPdfRenderer> log) : IPdfRenderer, IAsyncDisposable
{
    readonly SemaphoreSlim _gate = new(1, 1);
    IPlaywright? _playwright;
    IBrowser? _browser;

    async Task<IBrowser> BrowserAsync()
    {
        if (_browser is { IsConnected: true }) return _browser;
        await _gate.WaitAsync();
        try
        {
            if (_browser is { IsConnected: true }) return _browser;
            if (_browser != null) await _browser.DisposeAsync();
            _playwright ??= await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                // Containers give /dev/shm only a little space.
                Args = ["--disable-dev-shm-usage", "--disable-gpu", "--font-render-hinting=none"],
            });
            log.LogInformation("PDF browser started (Chromium {Version})", _browser.Version);
            return _browser;
        }
        finally { _gate.Release(); }
    }

    public async Task<byte[]> LetterAsync(string title, string html, bool rtl, byte[]? logo, CancellationToken ct = default)
    {
        var browser = await BrowserAsync();
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions { JavaScriptEnabled = false, Locale = rtl ? "ar-AE" : "en-AE" });
        // Nothing is fetched from anywhere: a template cannot make the server call out.
        await context.RouteAsync("**/*", r => r.Request.Url.StartsWith("data:", StringComparison.Ordinal) ? r.ContinueAsync() : r.AbortAsync());
        var page = await context.NewPageAsync();
        await page.SetContentAsync(Page(title, html, rtl, logo), new PageSetContentOptions { WaitUntil = WaitUntilState.Load, Timeout = 30000 });
        ct.ThrowIfCancellationRequested();
        var footer = $"<div style=\"width:100%;font-family:'Noto Sans','Noto Kufi Arabic',sans-serif;font-size:8px;color:#6b6b6b;text-align:center\">{WebUtility.HtmlEncode(title)}  |  <span class=\"pageNumber\"></span> / <span class=\"totalPages\"></span></div>";
        return await page.PdfAsync(new PagePdfOptions
        {
            Format = "A4", PrintBackground = true, DisplayHeaderFooter = true, HeaderTemplate = "<span></span>", FooterTemplate = footer,
            Margin = new Margin { Top = "2cm", Bottom = "2cm", Left = "2cm", Right = "2cm" },
        });
    }

    /// <summary>The printable page: the logo, then the letter. Email-sized wrappers are widened to the page.</summary>
    public static string Page(string title, string html, bool rtl, byte[]? logo)
    {
        var logoTag = logo is { Length: > 0 } && ImageType(logo) is { } type
            ? $"<header><img src=\"data:{type};base64,{Convert.ToBase64String(logo)}\" alt=\"\"></header>" : "";
        return $$"""
            <!doctype html>
            <html lang="{{(rtl ? "ar" : "en")}}" dir="{{(rtl ? "rtl" : "ltr")}}">
            <head>
            <meta charset="utf-8">
            <title>{{WebUtility.HtmlEncode(title)}}</title>
            <style>
              @page { size: A4; }
              html, body { margin: 0; padding: 0; background: #fff; }
              body {
                font-family: {{(rtl ? "'Noto Kufi Arabic', 'Noto Naskh Arabic', 'Noto Sans Arabic', 'Noto Sans'" : "'Noto Sans', 'Noto Kufi Arabic', 'Noto Naskh Arabic'")}}, 'DejaVu Sans', sans-serif;
                font-size: 11pt; line-height: 1.7; color: #232528;
                -webkit-print-color-adjust: exact; print-color-adjust: exact;
              }
              header { margin-bottom: 24px; text-align: left; }
              header img { max-height: 56px; max-width: 220px; }
              /* The letter HTML is made for email (a narrow column in Arial): on paper it takes the page and the ministry fonts. */
              main > div { max-width: none !important; font-family: inherit !important; font-size: inherit !important; }
              h1, h2, h3 { line-height: 1.4; page-break-after: avoid; }
              p, li { orphans: 3; widows: 3; }
              table { border-collapse: collapse; max-width: 100%; }
              img { max-width: 100%; }
            </style>
            </head>
            <body>{{logoTag}}<main>{{html}}</main></body>
            </html>
            """;
    }

    /// <summary>The image type from its first bytes (PNG, JPEG, GIF or WebP); null for anything else.</summary>
    static string? ImageType(byte[] b) =>
        b.Length > 12 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ? "image/png"
        : b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "image/jpeg"
        : b.Length > 6 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 ? "image/gif"
        : b.Length > 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50 ? "image/webp"
        : null;

    public async ValueTask DisposeAsync()
    {
        if (_browser != null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        _gate.Dispose();
    }
}
