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
/// Reads CV text: the PDF text layer and Word documents directly; scans and photos through the OCR service
/// (OCR_URL, the PaddleOCR container) when it is configured.
/// </summary>
public sealed class CvReader(HttpClient http, IConfiguration cfg) : ICvReader
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
            var ocr = await OcrAsync(content, "application/pdf", ct);
            return ocr ?? new CvText(text, "pdf-text");
        }
        if (ext is "png" or "jpg" or "jpeg")
            return await OcrAsync(content, ext == "png" ? "image/png" : "image/jpeg", ct) ?? new CvText("", "none");
        return new CvText("", "none");
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
