"""Pure helpers for the careers portal (no Frappe imports, unit-tested)."""
import hashlib
import re

LANGS = ("ar", "en")
DEFAULT_LANG = "ar"

# Job Applicant.status -> what the candidate sees (never AI scores or internal notes)
STATUS = {
    "Open": ("Under review", "قيد المراجعة", "info"),
    "Hold": ("Under review", "قيد المراجعة", "info"),
    "Replied": ("We have contacted you", "تم التواصل معك", "warning"),
    "Accepted": ("Successful", "مقبول", "success"),
    "Rejected": ("Not selected", "لم يتم الاختيار", "error"),
}
INTERVIEW = ("Interview scheduled", "تم تحديد مقابلة", "success")

CV_TYPES = {
    "pdf": (b"%PDF",),
    "docx": (b"PK\x03\x04",),
    "png": (b"\x89PNG\r\n\x1a\n",),
    "jpg": (b"\xff\xd8\xff",),
    "jpeg": (b"\xff\xd8\xff",),
}


def pick_lang(requested: str | None, cookie: str | None) -> str:
    for value in (requested, cookie):
        if value in LANGS:
            return value
    return DEFAULT_LANG


def text(lang: str, en: str, ar: str) -> str:
    return ar if lang == "ar" else en


def localized(lang: str, value_en: str | None, value_ar: str | None) -> str:
    """Bilingual content field with fallback to whichever language is filled in."""
    if lang == "ar":
        return (value_ar or "").strip() or (value_en or "").strip()
    return (value_en or "").strip() or (value_ar or "").strip()


def candidate_status(status: str | None, has_interview: bool = False) -> dict:
    en, ar, tone = INTERVIEW if has_interview and status in ("Open", "Hold", "Replied") else \
        STATUS.get(status or "Open", STATUS["Open"])
    return {"en": en, "ar": ar, "tone": tone}


def clean_department(name: str | None) -> str:
    """ERPNext suffixes departments with ' - <company abbr>'."""
    return re.sub(r"\s+-\s+[A-Z0-9&]{1,10}$", "", (name or "").strip())


def validate_cv(filename: str, content: bytes, max_mb: int) -> str:
    """Returns the normalised extension or raises ValueError with a user-safe message key."""
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


def safe_redirect(path: str | None, default: str = "/careers") -> str:
    """Only allow same-site paths under /careers (prevents open redirects)."""
    path = (path or "").strip()
    if path.startswith("/careers") and not path.startswith("//") and "\\" not in path and "://" not in path:
        return path
    return default


PHONE_RE = re.compile(r"^\+?[0-9][0-9 ()-]{6,19}$")


def valid_phone(phone: str) -> bool:
    return bool(PHONE_RE.match((phone or "").strip()))


def reference(name: str, creation) -> str:
    """Stable, human-friendly application reference for support calls."""
    return f"MOET-{str(creation)[:4]}-{hashlib.sha1((name or '').encode()).hexdigest()[:6].upper()}"
