"""Pure helpers for the careers portal API (no Frappe imports, unit-tested)."""
import hashlib
import re

# Job Applicant.status -> status code the portal shows (never scores or internal notes).
# The portal turns the code into text in the visitor's language.
STATUS = {
    "Open": ("under_review", "info"),
    "Hold": ("under_review", "info"),
    "Replied": ("contacted", "warning"),
    "Accepted": ("successful", "success"),
    "Rejected": ("not_selected", "error"),
}
INTERVIEW = ("interview", "success")

CV_TYPES = {
    "pdf": (b"%PDF",),
    "docx": (b"PK\x03\x04",),
    "png": (b"\x89PNG\r\n\x1a\n",),
    "jpg": (b"\xff\xd8\xff",),
    "jpeg": (b"\xff\xd8\xff",),
}

RTL_LANGS = {"ar", "he", "fa", "ur", "ps", "ckb", "ku", "dv", "yi", "sd", "ug"}
LANG_RE = re.compile(r"^[a-z]{2,3}(-[A-Za-z]{2,4})?$")


def candidate_status(status: str | None, has_interview: bool = False) -> dict:
    code, tone = INTERVIEW if has_interview and status in ("Open", "Hold", "Replied") else \
        STATUS.get(status or "Open", STATUS["Open"])
    return {"code": code, "tone": tone}


def is_rtl(lang: str | None) -> bool:
    return (lang or "").split("-")[0].lower() in RTL_LANGS


def valid_lang(lang: str | None) -> bool:
    return bool(LANG_RE.match(lang or ""))


def clean_department(name: str | None) -> str:
    """ERPNext suffixes departments with ' - <company abbr>'."""
    return re.sub(r"\s+-\s+[A-Z0-9&]{1,10}$", "", (name or "").strip())


def validate_cv(filename: str, content: bytes, max_mb: int) -> str:
    """Returns the normalised extension or raises ValueError('type'|'size'|'empty')."""
    ext = (filename or "").rsplit(".", 1)[-1].lower() if "." in (filename or "") else ""
    if ext not in CV_TYPES:
        raise ValueError("type")
    if not content:
        raise ValueError("empty")
    if len(content) > max_mb * 1024 * 1024:
        raise ValueError("size")
    if not any(content.startswith(sig) for sig in CV_TYPES[ext]):
        raise ValueError("type")
    return ext


PHONE_RE = re.compile(r"^\+?[0-9][0-9 ()-]{6,19}$")


def valid_phone(phone: str) -> bool:
    return bool(PHONE_RE.match((phone or "").strip()))


def reference(name: str, creation) -> str:
    """Stable, human-friendly application reference for support calls."""
    return f"MOET-{str(creation)[:4]}-{hashlib.sha1((name or '').encode()).hexdigest()[:6].upper()}"


def content_hash(*parts) -> str:
    return hashlib.sha1("\x1f".join(str(p or "") for p in parts).encode()).hexdigest()[:16]


def pick_translation(base_title: str, base_desc: str, row: dict | None) -> tuple:
    """Approved translation fields win; anything missing falls back to the job's original text."""
    row = row or {}
    if row.get("status") != "Approved":
        return base_title or "", base_desc or ""
    return (row.get("job_title") or "").strip() or base_title or "", (row.get("description") or "").strip() or base_desc or ""


def merge_text(base: dict, *overlays: dict) -> dict:
    """UI text: English base, then the language file, then approved HR overrides. Empty values are ignored."""
    out = dict(base)
    for o in overlays:
        out.update({k: v for k, v in (o or {}).items() if k in base and isinstance(v, str) and v.strip()})
    return out


def summary_text(html: str, limit: int = 240) -> str:
    """Plain-text teaser from HR's HTML: block ends become spaces, cut on a word boundary."""
    text = re.sub(r"<(br|/p|/li|/h[1-6]|/div|/tr)[^>]*>", " ", html or "", flags=re.I)
    text = re.sub(r"<[^>]+>", "", text)
    text = re.sub(r"&nbsp;", " ", text)
    text = re.sub(r"\s+", " ", text).strip()
    if len(text) <= limit:
        return text
    cut = text[:limit].rsplit(" ", 1)[0].rstrip(",;:.")
    return cut + "…"
