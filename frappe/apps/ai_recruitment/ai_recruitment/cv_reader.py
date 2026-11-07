"""Read any CV, Arabic or English, ATS-style or not.

Auto mode:
- ATS-style PDF/DOCX (single column, real text layer): use the text layer.
- Scanned PDF or image: OCR (vision model first, Tesseract as fallback).
- Designed / multi-column PDF: the text layer scrambles reading order, so read the
  rendered pages with the vision model (fallback: text layer).
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


def _ocr(data: bytes, kind: str, prefer_vision: bool) -> tuple[str, str]:
    from ai_recruitment.llm import is_configured
    from ai_recruitment.ocr import available_engines, ocr

    if prefer_vision and is_configured():
        try:
            text = ocr(data, kind, "Vision Model")
            if len(text.strip()) >= 50:
                return text, "vision"
        except Exception:
            frappe.log_error(title="AI CV vision read failed; trying Tesseract")
    if available_engines()["tesseract"]:
        return ocr(data, kind, "Tesseract"), "tesseract"
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
                    if mode == "Tesseract" and layout == "scanned":
                        ocr_text, how = _ocr(data, "pdf", prefer_vision=False)
                    elif mode in ("Auto", "Vision Model"):
                        ocr_text, how = _ocr(data, "pdf", prefer_vision=True)
                    else:
                        ocr_text, how = "", "none"
                    # For designed CVs keep the text layer if the vision read failed.
                    if len(ocr_text.strip()) >= 50:
                        text, method = ocr_text, how
            elif ext == "docx":
                text, method, layout = _docx_text(data), "docx", "document"
            elif ext in ("txt", "md"):
                text, method, layout = data.decode("utf-8", "ignore"), "text", "text"
            elif ext in IMAGE_EXT and mode != "Off":
                text, method = _ocr(data, ext, prefer_vision=mode in ("Auto", "Vision Model"))
                layout = "image"
        except Exception:
            frappe.log_error(title=f"CV reading failed: {app.get('name')}")
    parts = [text]
    if app.get("cover_letter"):
        parts.append(strip_html(app.get("cover_letter")))
    return {"text": "\n\n".join(p for p in parts if p and p.strip()), "method": method, "layout": layout}
