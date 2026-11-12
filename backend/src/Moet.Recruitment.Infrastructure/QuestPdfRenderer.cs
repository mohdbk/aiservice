using Moet.Recruitment.Application.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Moet.Recruitment.Infrastructure;

/// <summary>
/// Letters as A4 PDFs with QuestPDF (it shapes Arabic and lays it out right to left). The licence type comes from PDF_LICENSE
/// (Community by default; see docs/ON_PREM.md). Fonts: the container installs Noto (Naskh Arabic and Sans); other systems use
/// what they have, falling back in order.
/// </summary>
public sealed class QuestPdfRenderer : IPdfRenderer
{
    static readonly string[] Fonts = ["Noto Kufi Arabic", "Noto Naskh Arabic", "Noto Sans Arabic", "Noto Sans", "DejaVu Sans", "Arial"];
    static readonly object Gate = new();
    static bool registered;
    /// <summary>The font files registered (for the health page and the logs).</summary>
    public static IReadOnlyList<string> Registered { get; private set; } = [];

    /// <summary>
    /// Registers the font files directly (QuestPDF does not always see the system's fonts in a container): any .ttf or .otf
    /// under FONTS_DIR, the app's own fonts folder, then the Noto Arabic and Latin families under /usr/share/fonts.
    /// </summary>
    static void RegisterFonts(string? extraDir)
    {
        lock (Gate)
        {
            if (registered) return;
            registered = true;
            var names = new List<string>();
            var dirs = new[] { extraDir, Path.Combine(AppContext.BaseDirectory, "fonts"), "/usr/share/fonts", "/usr/local/share/fonts" }
                .Where(d => !string.IsNullOrWhiteSpace(d) && Directory.Exists(d)).Distinct();
            foreach (var dir in dirs)
            {
                var system = dir!.StartsWith("/usr/", StringComparison.Ordinal);
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)).ToList(); }
                catch (Exception) { continue; }
                foreach (var f in files)
                {
                    var name = Path.GetFileName(f);
                    // From the system only the families the letters use (there can be hundreds of fonts there).
                    if (system && !(name.StartsWith("NotoKufiArabic", StringComparison.Ordinal) || name.StartsWith("NotoNaskhArabic", StringComparison.Ordinal)
                        || name.StartsWith("NotoSansArabic", StringComparison.Ordinal) || name.StartsWith("NotoSans-", StringComparison.Ordinal)
                        || name.StartsWith("DejaVuSans", StringComparison.Ordinal))) continue;
                    try
                    {
                        using var stream = File.OpenRead(f);
                        QuestPDF.Drawing.FontManager.RegisterFont(stream);
                        names.Add(name);
                    }
                    catch (Exception) { /* not a font QuestPDF can read: skipped */ }
                }
            }
            Registered = names;
        }
    }

    public QuestPdfRenderer(Microsoft.Extensions.Configuration.IConfiguration cfg)
    {
        QuestPDF.Settings.License = (cfg["PDF_LICENSE"] ?? "").ToLowerInvariant() switch
        {
            "professional" => LicenseType.Professional,
            "enterprise" => LicenseType.Enterprise,
            _ => LicenseType.Community,
        };
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
        RegisterFonts(cfg["FONTS_DIR"]);
    }

    public byte[] Letter(string title, string text, bool rtl, byte[]? logo)
    {
        var blocks = text.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(s => s.FontFamily(Fonts).FontSize(11).LineHeight(1.5f).FontColor("#232528"));
            if (rtl) page.ContentFromRightToLeft();
            page.Header().PaddingBottom(16).Row(row =>
            {
                if (logo is { Length: > 0 })
                {
                    try { row.ConstantItem(140).Height(48).AlignLeft().Image(logo).FitArea(); }
                    catch (ArgumentException) { /* not an image QuestPDF can read: the letter goes without it */ }
                }
                row.RelativeItem();
            });
            page.Content().Column(col =>
            {
                col.Spacing(10);
                foreach (var b in blocks)
                {
                    if (b.StartsWith("# ", StringComparison.Ordinal)) col.Item().PaddingBottom(4).Text(b[2..].Trim()).FontSize(16).Bold();
                    else col.Item().Text(b);
                }
            });
            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor("#6b6b6b"));
                t.Span(title);
                t.Span("  |  ");
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        })).GeneratePdf();
    }
}
