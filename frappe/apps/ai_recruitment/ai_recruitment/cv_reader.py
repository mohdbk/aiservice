"""Read any CV, Arabic or English, ATS-style or not.

Pipeline (the OCR benchmark winner, see docs/ON_PREM.md: PaddleOCR draft corrected by the vision model):
- ATS-style PDF (single column, real text layer): use the text layer.
- Designed / multi-column PDF: the text layer has exact characters but scrambled order, so it is
  used as the draft and the vision model rewrites it in reading order from the page images.
- Scanned PDF or image: PaddleOCR PP-OCRv5 draft corrected by the vision model.
If one half is unavailable the other is used alone (see ocr.read).
DOCX: reads paragraphs, tables and text boxes in document order.
"""
import io
import re

import frappe

from ai_recruitment.setup import setting
from ai_recruitment.utils import looks_multicolumn, strip_html

IMAGE_EXT = ("png", "jpg", "jpeg", "tif", "tiff", "webp")
W_NS = "{http://schemas.openxmlformats.org/wordprocessingml/2006/main}"


def _file_bytes(file_url: str) -> bytes:
    name = frappe.db.get_value("File", {"file_url": file_url}, "name")
    if not name:
        return b""
    content = frappe.get_doc("File", name).get_content()
    return content.encode() if isinstance(content, str) else content


def _pdf_text_and_layout(data: bytes):
    from pypdf import PdfReader

    reader = PdfReader(io.BytesIO(data))
    texts, multicol, pages = [], False, len(reader.pages)
    for page in reader.pages[:5]:
        xs = []

        def visitor(text, cm, tm, font, size, xs=xs):
            if text and text.strip() and len(text.strip()) > 1:
                try:
                    xs.append(float(tm[4]) * float(cm[0]) + float(cm[4]))
                except (TypeError, ValueError, IndexError):
                    pass

        texts.append(page.extract_text(visitor_text=visitor) or "")
        try:
            width = float(page.mediabox.width)
        except Exception:
            width = 0
        multicol = multicol or looks_multicolumn(xs, width)
    for page in reader.pages[5:]:
        texts.append(page.extract_text() or "")
    return "\n".join(texts), multicol, pages


def _docx_text(data: bytes) -> str:
    """All w:t text in document order: paragraphs, tables and text boxes (designed DOCX CVs)."""
    import docx

    body = docx.Document(io.BytesIO(data)).element.body
    out, line = [], []
    for el in body.iter():
        tag = el.tag
        if tag == W_NS + "t" and el.text:
            line.append(el.text)
        elif tag in (W_NS + "p", W_NS + "tc"):
            if line:
                out.append("".join(line))
                line = []
    if line:
        out.append("".join(line))
    seen, uniq = set(), []
    for ln in out:  # text boxes are often duplicated as fallback content
        key = ln.strip()
        if key and key not in seen:
            seen.add(key)
            uniq.append(key)
    return "\n".join(uniq)


def _ocr(data: bytes, kind: str, text_layer: str | None = None) -> tuple:
    from ai_recruitment.ocr import read

    try:
        text, how = read(data, kind, text_layer=text_layer)
        return (text, how) if len(text.strip()) >= 50 else ("", "none")
    except Exception:
        frappe.log_error(title="AI CV reading failed")
        return "", "none"


def read_cv(app) -> dict:
    """Returns {text, method, layout} for the applicant's CV and cover letter."""
    mode = setting("ocr_mode", "Auto")
    url = app.get("resume_attachment")
    text, method, layout = "", "none", "none"
    if url:
        data = _file_bytes(url)
        ext = url.lower().rsplit(".", 1)[-1] if "." in url else ""
        try:
            if ext == "pdf":
                text, multicol, pages = _pdf_text_and_layout(data)
                per_page = len(re.sub(r"\s", "", text)) / max(pages, 1)
                if per_page < 200:
                    layout = "scanned"
                elif multicol:
                    layout = "designed"
                else:
                    layout = "ats"
                method = "text layer"
                if mode != "Off" and layout in ("scanned", "designed"):
                    ocr_text, how = _ocr(data, "pdf", text_layer=text if layout == "designed" else None)
                    # Keep the text layer if every reading step failed.
                    if len(ocr_text.strip()) >= 50:
                        text, method = ocr_text, how
            elif ext == "docx":
                text, method, layout = _docx_text(data), "docx", "document"
            elif ext in ("txt", "md"):
                text, method, layout = data.decode("utf-8", "ignore"), "text", "text"
            elif ext in IMAGE_EXT and mode != "Off":
                text, method = _ocr(data, ext)
                layout = "image"
        except Exception:
            frappe.log_error(title=f"CV reading failed: {app.get('name')}")
    parts = [text]
    if app.get("cover_letter"):
        parts.append(strip_html(app.get("cover_letter")))
    return {"text": "\n\n".join(p for p in parts if p and p.strip()), "method": method, "layout": layout}
