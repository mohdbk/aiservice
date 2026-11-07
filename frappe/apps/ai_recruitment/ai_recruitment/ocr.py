"""OCR for scanned CVs: local Tesseract (Arabic + English) or the LLM's vision input."""
import base64
import glob
import os
import shutil
import subprocess
import tempfile

import frappe

MAX_PAGES = 5


def available_engines() -> dict:
    return {"tesseract": bool(shutil.which("tesseract")), "pdftoppm": bool(shutil.which("pdftoppm"))}


def _pdf_to_pngs(data: bytes, tmp: str) -> list:
    pdf = os.path.join(tmp, "in.pdf")
    with open(pdf, "wb") as f:
        f.write(data)
    subprocess.run(["pdftoppm", "-r", "200", "-l", str(MAX_PAGES), "-png", pdf, os.path.join(tmp, "p")],
                   check=True, timeout=180, capture_output=True)
    return sorted(glob.glob(os.path.join(tmp, "p*.png")))


def _tesseract(path: str) -> str:
    r = subprocess.run(["tesseract", path, "stdout", "-l", "ara+eng"], capture_output=True, timeout=180)
    return r.stdout.decode("utf-8", "ignore")


def _vision(paths: list) -> str:
    from ai_recruitment.llm import chat_completion
    from ai_recruitment.setup import setting

    parts = [{"type": "text", "text": "Transcribe all text on these CV pages exactly, in reading order. "
                                      "Keep Arabic as Arabic. Output plain text only."}]
    for p in paths[:MAX_PAGES]:
        with open(p, "rb") as f:
            parts.append({"type": "image_url",
                          "image_url": {"url": "data:image/png;base64," + base64.b64encode(f.read()).decode()}})
    msg = chat_completion([{"role": "user", "content": parts}], temperature=0, timeout=300,
                          model=setting("vision_model", None))
    return msg.get("content") or ""


def ocr(data: bytes, kind: str, mode: str) -> str:
    """kind: 'pdf' or an image extension. mode: 'Tesseract' | 'Vision Model'."""
    eng = available_engines()
    with tempfile.TemporaryDirectory() as tmp:
        if kind == "pdf":
            if not eng["pdftoppm"]:
                frappe.log_error(title="AI OCR: pdftoppm not installed")
                return ""
            pages = _pdf_to_pngs(data, tmp)
        else:
            path = os.path.join(tmp, f"img.{kind}")
            with open(path, "wb") as f:
                f.write(data)
            pages = [path]
        if mode == "Vision Model":
            return _vision(pages)
        if not eng["tesseract"]:
            frappe.log_error(title="AI OCR: tesseract not installed")
            return ""
        return "\n\n".join(_tesseract(p) for p in pages)
