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
    static readonly string[] Fonts = ["Noto Naskh Arabic", "Noto Sans Arabic", "Noto Sans", "DejaVu Sans", "Arial"];

    public QuestPdfRenderer(Microsoft.Extensions.Configuration.IConfiguration cfg)
    {
        QuestPDF.Settings.License = (cfg["PDF_LICENSE"] ?? "").ToLowerInvariant() switch
        {
            "professional" => LicenseType.Professional,
            "enterprise" => LicenseType.Enterprise,
            _ => LicenseType.Community,
        };
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
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
