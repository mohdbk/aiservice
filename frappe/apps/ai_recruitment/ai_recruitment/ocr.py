"""CV OCR: PaddleOCR PP-OCRv5 draft corrected by the vision model (the OCR benchmark winner, see docs/ON_PREM.md).

If one half is unavailable the other is used alone, so a CV is never left unread:
- normal:              PaddleOCR draft (or a PDF's own text layer) + page images -> vision model
- vision model down:   PaddleOCR text as is
- PaddleOCR down:      vision model reads the page images alone
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
TEXT_LAYER_SOURCE = "the PDF's embedded text layer (exact characters, but the reading order may be scrambled)"


def paddle_available() -> bool:
    env_dir = os.path.dirname(os.path.dirname(PADDLE_PYTHON))
    return os.path.exists(PADDLE_PYTHON) and os.path.exists(os.path.join(env_dir, ".ready"))


def available_engines() -> dict:
    return {"paddle": paddle_available(), "pdftoppm": bool(shutil.which("pdftoppm"))}


# ---------------- pages ----------------
def _pages(data: bytes, kind: str, tmp: str) -> list:
    if kind == "pdf":
        if not shutil.which("pdftoppm"):
            frappe.log_error(title="AI OCR: pdftoppm (poppler-utils) not installed")
            return []
        pdf = os.path.join(tmp, "in.pdf")
        with open(pdf, "wb") as f:
            f.write(data)
        subprocess.run(["pdftoppm", "-r", "200", "-l", str(MAX_PAGES), "-png", pdf, os.path.join(tmp, "p")],
                       check=True, timeout=180, capture_output=True)
        return sorted(glob.glob(os.path.join(tmp, "p*.png")))
    path = os.path.join(tmp, f"img.{kind}")
    with open(path, "wb") as f:
        f.write(data)
    return [path]


def _images(pages: list) -> list:
    parts = []
    for p in pages[:MAX_PAGES]:
        with open(p, "rb") as f:
            parts.append({"type": "image_url",
                          "image_url": {"url": "data:image/png;base64," + base64.b64encode(f.read()).decode()}})
    return parts


# ---------------- the two halves ----------------
def _paddle(pages: list) -> str:
    """PaddleOCR PP-OCRv5 in its own Python environment (see paddle_runner.read_text)."""
    r = subprocess.run([PADDLE_PYTHON, RUNNER, "text", *pages], capture_output=True, timeout=900,
                       env={**os.environ, "PYTHONIOENCODING": "utf-8"})
    out = r.stdout.decode("utf-8", "ignore")
    if r.returncode != 0 or "@@JSON@@" not in out:
        raise RuntimeError(f"PaddleOCR failed: {r.stderr.decode('utf-8', 'ignore')[-500:]}")
    return json.loads(out.rsplit("@@JSON@@", 1)[1])["text"]


def _vision_call(prompt: str, pages: list) -> str:
    from ai_recruitment.llm import chat_completion
    from ai_recruitment.setup import setting

    msg = chat_completion([{"role": "user", "content": [{"type": "text", "text": prompt}, *_images(pages)]}],
                          temperature=0, timeout=300, model=setting("vision_model", None), max_tokens=8000)
    return msg.get("content") or ""


# ---------------- pipeline ----------------
def read(data: bytes, kind: str, text_layer: str | None = None, parts: str = "both") -> tuple:
    """Read CV pages. Returns (text, method).
    text_layer: a PDF's own text, used as the draft instead of PaddleOCR (designed PDFs).
    parts: 'both' (production), or 'paddleocr' / 'vision' to run one half alone (AI OCR Test)."""
    from ai_recruitment.llm import is_configured

    with tempfile.TemporaryDirectory() as tmp:
        pages = _pages(data, kind, tmp)
        if not pages:
            return "", "none"
        use_model = parts in ("both", "vision") and is_configured()
        use_paddle = parts in ("both", "paddleocr") and paddle_available()

        draft, source, label = None, "", ""
        if text_layer and len(text_layer.strip()) >= 50 and parts == "both":
            draft, source, label = text_layer, TEXT_LAYER_SOURCE, "text layer"
        elif use_paddle:
            try:
                draft, source, label = _paddle(pages), "the PaddleOCR engine", "paddleocr"
            except Exception:
                frappe.log_error(title="AI OCR: PaddleOCR failed")

        if use_model:
            try:
                if draft is not None and len(draft.strip()) >= 20:
                    prompt = HYBRID_OCR_PROMPT.format(source=source, draft=draft.strip()[:20000])
                    return hybrid_result(_vision_call(prompt, pages), draft), f"{label} + vision model"
                text = _vision_call(VISION_OCR_PROMPT, pages)
                if not has_repetition_loop(text):
                    return text, "vision model"
            except Exception:
                frappe.log_error(title="AI OCR: vision model failed")
        if draft is not None:
            return draft, label
        return "", "none"
