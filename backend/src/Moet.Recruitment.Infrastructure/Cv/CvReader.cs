using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Configuration;
using Moet.Recruitment.Application.Common;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Moet.Recruitment.Infrastructure.Cv;

/// <summary>
/// Reads CV text: the PDF text layer and Word documents directly; scans and photos through the OCR service (OCR_URL)
/// when one is set up, and otherwise through the AI model, if it accepts images (the page images of a scanned PDF, or
/// the photo itself).
/// </summary>
public sealed class CvReader(HttpClient http, IConfiguration cfg, ILanguageModel model) : ICvReader
{
    const int MinUsefulChars = 200;

    public async Task<CvText> ReadAsync(byte[] content, string extension, CancellationToken ct = default)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        if (ext == "docx") return new CvText(Docx(content), "word");
        if (ext == "pdf")
        {
            var text = PdfText(content);
            if (text.Trim().Length >= MinUsefulChars) return new CvText(text, "pdf-text");
            var ocr = await OcrAsync(content, "application/pdf", ct) ?? await ModelAsync(PdfImages(content), ct);
            return ocr ?? new CvText(text, "pdf-text");
        }
        if (ext is "png" or "jpg" or "jpeg")
        {
            var type = ext == "png" ? "image/png" : "image/jpeg";
            return await OcrAsync(content, type, ct) ?? await ModelAsync([new ImageInput(content, type)], ct) ?? new CvText("", "none");
        }
        return new CvText("", "none");
    }

    const int MaxPages = 5;
    const int MaxImageBytes = 8 * 1024 * 1024;

    /// <summary>The text of CV images read by the AI model, or null (no model for images, or nothing readable).</summary>
    async Task<CvText?> ModelAsync(IReadOnlyList<ImageInput> images, CancellationToken ct)
    {
        var usable = images.Where(i => i.Data.Length is > 0 and <= MaxImageBytes).Take(MaxPages).ToList();
        if (usable.Count == 0) return null;
        try
        {
            var text = await model.ReadImagesAsync(usable, ct);
            return string.IsNullOrWhiteSpace(text) ? null : new CvText(text, "model");
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;   // the model cannot read images, or is unreachable: the CV is marked unreadable for a person
        }
    }

    /// <summary>The scanned page images inside a PDF (one per page for most scans), as JPEG or PNG.</summary>
    static List<ImageInput> PdfImages(byte[] content)
    {
        var list = new List<ImageInput>();
        try
        {
            using var doc = PdfDocument.Open(content);
            foreach (var page in doc.GetPages().Take(MaxPages))
            {
                var best = page.GetImages().OrderByDescending(i => i.WidthInSamples * i.HeightInSamples).FirstOrDefault();
                if (best == null) continue;
                var raw = best.RawBytes.ToArray();
                if (raw.Length > 3 && raw[0] == 0xFF && raw[1] == 0xD8) list.Add(new ImageInput(raw, "image/jpeg"));
                else if (best.TryGetPng(out var png) && png.Length > 0) list.Add(new ImageInput(png, "image/png"));
            }
        }
        catch (Exception)
        {
            // an unreadable PDF: no images
        }
        return list;
    }

    static string PdfText(byte[] content)
    {
        try
        {
            using var doc = PdfDocument.Open(content);
            var sb = new StringBuilder();
            foreach (var page in doc.GetPages())
            {
                sb.AppendLine(ContentOrderTextExtractor.GetText(page));
                if (sb.Length > 200_000) break;
            }
            return sb.ToString();
        }
        catch (Exception)
        {
            return "";
        }
    }

    static string Docx(byte[] content)
    {
        try
        {
            using var ms = new MemoryStream(content);
            using var doc = WordprocessingDocument.Open(ms, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null) return "";
            var sb = new StringBuilder();
            foreach (var p in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
                sb.AppendLine(p.InnerText);
            return sb.ToString();
        }
        catch (Exception)
        {
            return "";
        }
    }

    async Task<CvText?> OcrAsync(byte[] content, string contentType, CancellationToken ct)
    {
        var url = (cfg["OCR_URL"] ?? "").TrimEnd('/');
        if (url.Length == 0) return null;
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", contentType == "application/pdf" ? "cv.pdf" : "cv.img");
        using var res = await http.PostAsync($"{url}/ocr", form, ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var text = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return text.Trim().Length == 0 ? null : new CvText(text, "ocr");
    }
}
