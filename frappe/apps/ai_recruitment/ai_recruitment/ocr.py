"""OCR engines for CVs.

- Tesseract (local, Arabic + English)
- PaddleOCR PP-OCRv5 (local CPU, Arabic + English; runs in its own Python 3.12 environment)
- Vision Model (the LLM reads page images)
- Hybrid: an OCR draft (or a PDF's own text layer) plus the page images go to the vision model,
  which corrects characters and reading order. Falls back to the draft if the model loops.
"""
import base64
import glob
import json
import os
import shutil
import subprocess
import tempfile

import frappe

from ai_recruitment.utils import HYBRID_OCR_PROMPT, VISION_OCR_PROMPT, has_repetition_loop, hybrid_result

MAX_PAGES = 5
PADDLE_PYTHON = os.environ.get("PADDLEOCR_PYTHON", "/home/frappe/paddleocr-env/bin/python")
RUNNER = os.path.join(os.path.dirname(__file__), "paddle_runner.py")


def paddle_available() -> bool:
    return os.path.exists(PADDLE_PYTHON) and os.path.exists(os.path.join(os.path.dirname(os.path.dirname(PADDLE_PYTHON)), ".ready"))


def available_engines() -> dict:
    return {"tesseract": bool(shutil.which("tesseract")), "pdftoppm": bool(shutil.which("pdftoppm")),
            "paddle": paddle_available()}


# ---------------- pages ----------------
def _pdf_to_pngs(data: bytes, tmp: str) -> list:
    pdf = os.path.join(tmp, "in.pdf")
    with open(pdf, "wb") as f:
        f.write(data)
    subprocess.run(["pdftoppm", "-r", "200", "-l", str(MAX_PAGES), "-png", pdf, os.path.join(tmp, "p")],
                   check=True, timeout=180, capture_output=True)
    return sorted(glob.glob(os.path.join(tmp, "p*.png")))


def _pages(data: bytes, kind: str, tmp: str) -> list:
    if kind == "pdf":
        if not shutil.which("pdftoppm"):
            frappe.log_error(title="AI OCR: pdftoppm not installed")
            return []
        return _pdf_to_pngs(data, tmp)
    path = os.path.join(tmp, f"img.{kind}")
    with open(path, "wb") as f:
        f.write(data)
    return [path]


# ---------------- engines ----------------
def _tesseract(pages: list) -> str:
    out = []
    for p in pages:
        r = subprocess.run(["tesseract", p, "stdout", "-l", "ara+eng"], capture_output=True, timeout=180)
        out.append(r.stdout.decode("utf-8", "ignore"))
    return "\n\n".join(out)


def _paddle(pages: list) -> str:
    """PaddleOCR PP-OCRv5 in its own Python environment (see paddle_runner.read_text)."""
    r = subprocess.run([PADDLE_PYTHON, RUNNER, "text", *pages], capture_output=True, timeout=900,
                       env={**os.environ, "PYTHONIOENCODING": "utf-8"})
    out = r.stdout.decode("utf-8", "ignore")
    if r.returncode != 0 or "@@JSON@@" not in out:
        raise RuntimeError(f"PaddleOCR failed: {r.stderr.decode('utf-8', 'ignore')[-500:]}")
    return json.loads(out.rsplit("@@JSON@@", 1)[1])["text"]


def _images(pages: list) -> list:
    parts = []
    for p in pages[:MAX_PAGES]:
        with open(p, "rb") as f:
            parts.append({"type": "image_url",
                          "image_url": {"url": "data:image/png;base64," + base64.b64encode(f.read()).decode()}})
    return parts


def _vision(pages: list) -> str:
    from ai_recruitment.llm import chat_completion
    from ai_recruitment.setup import setting

    msg = chat_completion([{"role": "user", "content": [{"type": "text", "text": VISION_OCR_PROMPT}, *_images(pages)]}],
                          temperature=0, timeout=300, model=setting("vision_model", None), max_tokens=8000)
    text = msg.get("content") or ""
    if has_repetition_loop(text):
        raise RuntimeError("Vision model output repeated itself")
    return text


def _hybrid(pages: list, draft: str, source: str) -> str:
    """OCR draft + images -> corrected text. Returns the draft unchanged if the model output is unusable."""
    from ai_recruitment.llm import chat_completion
    from ai_recruitment.setup import setting

    draft = (draft or "").strip()
    prompt = HYBRID_OCR_PROMPT.format(source=source, draft=draft[:20000] or "(empty)")
    try:
        msg = chat_completion([{"role": "user", "content": [{"type": "text", "text": prompt}, *_images(pages)]}],
                              temperature=0, timeout=300, model=setting("vision_model", None), max_tokens=8000)
    except Exception:
        frappe.log_error(title="AI OCR: hybrid correction failed, using draft")
        return draft
    return hybrid_result(msg.get("content"), draft)


def local_ocr(pages: list) -> tuple:
    """Best available local OCR: (text, engine name)."""
    if paddle_available():
        try:
            return _paddle(pages), "paddleocr"
        except Exception:
            frappe.log_error(title="AI OCR: PaddleOCR failed, trying Tesseract")
    if shutil.which("tesseract"):
        return _tesseract(pages), "tesseract"
    return "", "none"


def read(data: bytes, kind: str, mode: str, draft: str | None = None, draft_source: str = "") -> tuple:
    """Returns (text, method). kind: 'pdf' or an image extension.
    mode: 'Hybrid' | 'Vision Model' | 'PaddleOCR' | 'Tesseract' | 'Local' (best local engine).
    For Hybrid, pass `draft` (e.g. a PDF text layer) or leave it empty to use the best local OCR."""
    with tempfile.TemporaryDirectory() as tmp:
        pages = _pages(data, kind, tmp)
        if not pages:
            return "", "none"
        if mode == "Vision Model":
            return _vision(pages), "vision model"
        if mode == "PaddleOCR":
            return (_paddle(pages), "paddleocr") if paddle_available() else ("", "none")
        if mode == "Tesseract":
            return (_tesseract(pages), "tesseract") if shutil.which("tesseract") else ("", "none")
        if mode == "Local":
            return local_ocr(pages)
        if mode == "Hybrid":
            label = "text layer"
            if draft is None:
                draft, label = local_ocr(pages)
                draft_source = {"paddleocr": "the PaddleOCR engine", "tesseract": "the Tesseract OCR engine"}.get(
                    label, "no OCR engine (empty)")
            return _hybrid(pages, draft, draft_source or "an OCR engine"), f"hybrid ({label} + vision model)"
        raise ValueError(f"Unknown OCR mode {mode}")


def ocr(data: bytes, kind: str, mode: str, draft: str | None = None, draft_source: str = "") -> str:
    return read(data, kind, mode, draft, draft_source)[0]
