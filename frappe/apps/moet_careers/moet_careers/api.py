"""Candidate-facing endpoints. Candidates are Website Users: they never see Desk, AI scores or internal notes."""
import json

import frappe
from frappe.rate_limiter import rate_limit
from frappe.utils import now_datetime, validate_email_address

from moet_careers.portal_utils import candidate_status, localized, text, valid_phone, validate_cv

SETTINGS = "MoET Careers Settings"


def _lang():
    lang = frappe.form_dict.get("lang") or (frappe.request.cookies.get("moet_lang") if frappe.request else None)
    return lang if lang in ("ar", "en") else "ar"


def _fail(en, ar, exc=frappe.ValidationError):
    frappe.throw(text(_lang(), en, ar), exc)


# ---------------- account ----------------
@frappe.whitelist(allow_guest=True, methods=["POST"])
@rate_limit(limit=5, seconds=3600)
def signup(full_name: str, email: str, password: str):
    s = frappe.get_cached_doc(SETTINGS)
    if not s.allow_signup:
        _fail("Sign-up is currently closed.", "التسجيل مغلق حاليا.")
    full_name = (full_name or "").strip()
    email = (email or "").strip().lower()
    if not (2 <= len(full_name) <= 140):
        _fail("Please enter your full name.", "يرجى إدخال اسمك الكامل.")
    if not validate_email_address(email):
        _fail("Please enter a valid email address.", "يرجى إدخال بريد إلكتروني صحيح.")
    if len(password or "") < 8:
        _fail("Use a password of at least 8 characters.", "استخدم كلمة مرور من 8 أحرف على الأقل.")
    if frappe.db.exists("User", email):
        _fail("An account with this email already exists. Please sign in.",
              "يوجد حساب بهذا البريد الإلكتروني. يرجى تسجيل الدخول.")
    first, _, last = full_name.partition(" ")
    user = frappe.get_doc({
        "doctype": "User",
        "email": email,
        "first_name": first,
        "last_name": last,
        "user_type": "Website User",
        "send_welcome_email": 0,
        "enabled": 1,
        "language": _lang(),
        "new_password": password,
    })
    user.insert(ignore_permissions=True)
    return {"ok": True}


# ---------------- apply ----------------
@frappe.whitelist(methods=["POST"])
@rate_limit(limit=20, seconds=3600)
def apply(job: str, phone: str = "", country: str = "", cover_note: str = "", consent: str = "0"):
    if frappe.session.user == "Guest":
        _fail("Please sign in to apply.", "يرجى تسجيل الدخول للتقديم.", frappe.PermissionError)
    lang = _lang()
    s = frappe.get_cached_doc(SETTINGS)

    opening = frappe.db.get_value("Job Opening", job, ["name", "publish", "status", "closes_on", "designation", "job_title"],
                                  as_dict=True) if job else None
    if not opening or not opening.publish or opening.status != "Open" or (
            opening.closes_on and frappe.utils.getdate(opening.closes_on) < frappe.utils.getdate()):
        _fail("This job is no longer accepting applications.", "لم تعد هذه الوظيفة تستقبل طلبات.")
    if str(consent) not in ("1", "true", "on"):
        _fail("Please confirm that you have read how your application is processed.",
              "يرجى تأكيد اطلاعك على طريقة معالجة طلبك.")
    if phone and not valid_phone(phone):
        _fail("Please enter a valid phone number with the country code.", "يرجى إدخال رقم هاتف صحيح مع رمز الدولة.")

    user = frappe.get_doc("User", frappe.session.user)
    email = (user.email or user.name).lower()
    if frappe.db.exists("Job Applicant", {"email_id": email, "job_title": opening.name}):
        _fail("You have already applied for this job. You can follow it in My Applications.",
              "لقد تقدمت لهذه الوظيفة مسبقا. يمكنك متابعة طلبك في طلباتي.")

    f = frappe.request.files.get("cv") if frappe.request else None
    if not f:
        _fail("Please attach your CV.", "يرجى إرفاق سيرتك الذاتية.")
    content = f.stream.read()
    try:
        validate_cv(f.filename, content, int(s.max_cv_mb or 5))
    except ValueError as e:
        msgs = {
            "type": ("Upload your CV as PDF, Word (.docx), JPG or PNG.", "ارفع سيرتك الذاتية بصيغة PDF أو Word (.docx) أو JPG أو PNG."),
            "size": (f"The CV must be smaller than {int(s.max_cv_mb or 5)} MB.", f"يجب أن يكون حجم السيرة الذاتية أقل من {int(s.max_cv_mb or 5)} ميجابايت."),
            "empty": ("The file is empty.", "الملف فارغ."),
        }
        _fail(*msgs.get(str(e), msgs["type"]))

    doc = {
        "doctype": "Job Applicant",
        "applicant_name": (user.full_name or email)[:140],
        "email_id": email,
        "phone_number": (phone or "").strip()[:30],
        "job_title": opening.name,
        "designation": opening.designation,
        "status": "Open",
        "cover_letter": frappe.utils.escape_html((cover_note or "").strip()[:4000]),
        "applied_via_portal": 1,
        "consent_ai_screening": 1,
        "consent_at": now_datetime(),
        "portal_language": lang,
    }
    meta = frappe.get_meta("Job Applicant")
    if country and meta.has_field("country") and frappe.db.exists("Country", country):
        doc["country"] = country
    if meta.has_field("source") and frappe.db.exists("Job Applicant Source", "Website Listing"):
        doc["source"] = "Website Listing"
    applicant = frappe.get_doc(doc).insert(ignore_permissions=True)

    safe_name = frappe.scrub(user.full_name or "cv")[:40] or "cv"
    ext = f.filename.rsplit(".", 1)[-1].lower()
    file_doc = frappe.get_doc({
        "doctype": "File",
        "file_name": f"cv-{safe_name}.{ext}",
        "content": content,
        "is_private": 1,
        "attached_to_doctype": "Job Applicant",
        "attached_to_name": applicant.name,
        "attached_to_field": "resume_attachment",
    }).insert(ignore_permissions=True)
    frappe.db.set_value("Job Applicant", applicant.name, "resume_attachment", file_doc.file_url, update_modified=False)
    applicant.add_comment("Info", f"Applied via the careers portal ({lang}). Consent to AI-assisted screening recorded.")
    return {"application": applicant.name}


# ---------------- my application ----------------
def _own_applicant(name):
    if frappe.session.user == "Guest" or not name or not frappe.db.exists("Job Applicant", name):
        raise frappe.PermissionError
    a = frappe.get_doc("Job Applicant", name)
    email = (frappe.db.get_value("User", frappe.session.user, "email") or frappe.session.user).lower()
    if (a.email_id or "").lower() != email:
        raise frappe.PermissionError
    return a


def has_interview(applicant_name: str) -> bool:
    return bool(frappe.db.exists("Interview", {"job_applicant": applicant_name,
                                               "status": ["in", ["Pending", "Under Review"]]}))


@frappe.whitelist(methods=["GET", "POST"])
def application_profile(application: str):
    """What the system read from the candidate's CV. Excludes scores, summaries and gaps."""
    a = _own_applicant(application)
    st = candidate_status(a.status, has_interview(a.name))
    out = {"status": st, "reading": a.get("ai_status") in (None, "", "Pending"), "profile": None,
           "unreadable": a.get("ai_status") == "Failed"}
    raw = a.get("ai_profile")
    if raw:
        try:
            p = json.loads(raw)
        except ValueError:
            p = {}
        out["profile"] = {
            "years": p.get("years_of_experience"),
            "work": [{"position": w.get("position", ""), "employer": w.get("name") or w.get("description") or "",
                      "start": w.get("startDate", ""), "end": w.get("endDate", "")} for w in p.get("work") or []][:10],
            "education": [{"degree": " ".join(x for x in (e.get("studyType"), e.get("area")) if x),
                           "institution": e.get("institution", ""), "end": e.get("endDate", "")}
                          for e in p.get("education") or []][:6],
            "skills": list(dict.fromkeys([k for s in p.get("skills") or [] for k in (s.get("keywords") or [s.get("name")]) if k]))[:30],
            "languages": [f"{l.get('language')} ({l.get('fluency')})" if l.get("fluency") else l.get("language")
                          for l in p.get("languages") or [] if l.get("language")][:8],
            "certificates": [c.get("name") for c in p.get("certificates") or [] if c.get("name")][:10],
        }
    return out


@frappe.whitelist(methods=["POST"])
@rate_limit(limit=10, seconds=3600)
def add_correction(application: str, note: str):
    a = _own_applicant(application)
    note = (note or "").strip()
    if not (3 <= len(note) <= 2000):
        _fail("Please write a short note (up to 2000 characters).", "يرجى كتابة ملاحظة قصيرة (حتى 2000 حرف).")
    a.add_comment("Comment", "Candidate correction to the CV reading:<br>" + frappe.utils.escape_html(note),
                  comment_email=frappe.session.user, comment_by=frappe.utils.get_fullname(frappe.session.user))
    return {"ok": True}


def job_title_for(job_name: str, lang: str) -> str:
    j = frappe.db.get_value("Job Opening", job_name, ["job_title", "designation"] +
                            (["job_title_ar"] if frappe.get_meta("Job Opening").has_field("job_title_ar") else []),
                            as_dict=True) or {}
    return localized(lang, j.get("job_title"), j.get("job_title_ar")) or j.get("designation") or job_name
