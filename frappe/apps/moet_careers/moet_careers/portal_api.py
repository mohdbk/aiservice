"""API for the external careers portal.

Only the portal's backend calls these methods, authenticated with the API key of the service user (role
"Careers Portal Service"). The portal backend has already authenticated the applicant and passes their
email; browsers never talk to Frappe. Errors are returned as codes so the portal shows them in the visitor's
language: {"error": {"code": "...", "params": {...}}} with an HTTP 4xx status.
"""
import functools
import json

import frappe
from frappe.utils import getdate, now_datetime, today, validate_email_address
from frappe.utils.html_utils import clean_html

from moet_careers import __version__
from moet_careers.i18n import portal_languages, portal_text, source_language
from moet_careers.portal_utils import (candidate_status, clean_department, is_rtl, pick_translation, reference,
                                       valid_phone, validate_cv)

SETTINGS = "MoET Careers Settings"
SERVICE_ROLE = "Careers Portal Service"
PAGE_SIZE = 10


class Fail(Exception):
    def __init__(self, code: str, status: int = 400, **params):
        super().__init__(code)
        self.code, self.status, self.params = code, status, params


def endpoint(methods=("GET",)):
    def deco(fn):
        @functools.wraps(fn)
        def wrapper(*args, **kwargs):
            if SERVICE_ROLE not in frappe.get_roles():
                frappe.local.response.http_status_code = 403
                return {"error": {"code": "forbidden", "params": {}}}
            try:
                return fn(*args, **kwargs)
            except Fail as e:
                frappe.db.rollback()
                frappe.local.response.http_status_code = e.status
                return {"error": {"code": e.code, "params": e.params}}
        return frappe.whitelist(methods=list(methods))(wrapper)
    return deco


def _lang(lang: str | None) -> str:
    langs, default = portal_languages()
    codes = [l["code"] for l in langs]
    if lang in codes:
        return lang
    raise Fail("unknown_language", 404, languages=codes, default=default)


def _settings():
    return frappe.get_cached_doc(SETTINGS)


# ---------------- site ----------------
@endpoint()
def config(lang: str | None = None):
    """Languages, wording and settings. Without lang: only the language list (used for routing)."""
    langs, default = portal_languages()
    out = {"languages": langs, "default_language": default, "version": __version__}
    if not lang:
        return out
    lang = _lang(lang)
    s = _settings()
    out.update({
        "lang": lang,
        "dir": "rtl" if is_rtl(lang) else "ltr",
        "text": portal_text(lang),
        "main_site_url": s.main_site_url or "",
        "logo": s.logo or "",
        "logo_dark": s.logo_dark or "",
        "allow_signup": bool(s.allow_signup),
        "max_cv_mb": int(s.max_cv_mb or 5),
        "countries": _countries(),
    })
    return out


def _countries():
    names = frappe.get_all("Country", pluck="name", order_by="name asc")
    first = "United Arab Emirates"
    return ([first] if first in names else []) + [n for n in names if n != first]


# ---------------- jobs ----------------
def _has(field):
    return frappe.get_meta("Job Opening").has_field(field)


def _job_fields():
    fields = ["name", "job_title", "designation", "department", "closes_on", "posted_on", "description", "status", "publish"]
    return fields + [f for f in ("location", "employment_type", "lower_range", "upper_range", "currency",
                                 "publish_salary_range", "salary_per") if _has(f)]


def _base_filters():
    return [["publish", "=", 1], ["status", "=", "Open"]]


def _is_open(job) -> bool:
    return job.get("status") == "Open" and (not job.get("closes_on") or getdate(job["closes_on"]) >= getdate(today()))


def _translations(names: list, lang: str) -> dict:
    if not names:
        return {}
    rows = frappe.db.sql(
        """select parent, job_title, description, status from `tabJob Opening Translation`
           where parenttype='Job Opening' and language=%s and parent in %s""", (lang, tuple(names)), as_dict=True)
    return {r.parent: r for r in rows}


def _present(job, lang: str, row=None, full: bool = False) -> dict:
    title, desc = pick_translation(job.get("job_title") or job.get("designation") or job["name"],
                                   job.get("description") or "", row)
    translated = bool(row and row.get("status") == "Approved")
    content_lang = lang if translated else source_language(job.get("job_title") or "", job.get("description") or "")
    out = {
        "id": job["name"],
        "title": title,
        "content_lang": content_lang,
        "content_dir": "rtl" if is_rtl(content_lang) else "ltr",
        "department": clean_department(job.get("department")),
        "location": job.get("location") or "",
        "employment_type": job.get("employment_type") or "",
        "posted_on": str(job.get("posted_on") or "")[:10],
        "closes_on": str(job.get("closes_on") or "")[:10],
        "salary": None,
        "summary": frappe.utils.strip_html_tags(desc or "")[:240].strip(),
        "accepting": _is_open(job),
    }
    if job.get("publish_salary_range") and (job.get("lower_range") or job.get("upper_range")):
        out["salary"] = {"currency": job.get("currency") or "AED", "min": job.get("lower_range") or 0,
                         "max": job.get("upper_range") or 0, "per": job.get("salary_per") or ""}
    if full:
        out["description_html"] = clean_html(desc or "")
    return out


@endpoint()
def jobs(lang: str, q: str = "", department: str = "", location: str = "", page: int = 1):
    lang = _lang(lang)
    filters = _base_filters()
    if department:
        filters.append(["department", "like", f"{department}%"])
    if location and _has("location"):
        filters.append(["location", "=", location])
    or_filters = None
    q = (q or "").strip()[:100]
    if q:
        like = f"%{q}%"
        or_filters = [["job_title", "like", like], ["designation", "like", like], ["description", "like", like]]
        hits = frappe.db.sql_list(
            """select distinct parent from `tabJob Opening Translation` where parenttype='Job Opening'
               and status='Approved' and (job_title like %s or description like %s) limit 500""", (like, like))
        if hits:
            or_filters.append(["name", "in", hits])
    rows = frappe.get_all("Job Opening", fields=_job_fields(), filters=filters, or_filters=or_filters,
                          order_by="posted_on desc, creation desc", limit_page_length=500)
    rows = [r for r in rows if _is_open(r)]
    total, page = len(rows), max(1, int(page or 1))
    chunk = rows[(page - 1) * PAGE_SIZE: page * PAGE_SIZE]
    tr = _translations([r.name for r in chunk], lang)
    return {"jobs": [_present(r, lang, tr.get(r.name)) for r in chunk], "total": total, "page": page,
            "pages": max(1, -(-total // PAGE_SIZE)), "filters": _filter_options()}


def _filter_options():
    fields = ["department"] + (["location"] if _has("location") else [])
    rows = [r for r in frappe.get_all("Job Opening", fields=fields + ["closes_on", "status"], filters=_base_filters(),
                                      limit_page_length=500) if _is_open(r)]
    return {"departments": sorted({clean_department(r.department) for r in rows if r.department}),
            "locations": sorted({r.location for r in rows if r.get("location")})}


def _published_job(name: str):
    job = frappe.db.get_value("Job Opening", name, _job_fields(), as_dict=True) if name else None
    if not job or not job.publish:
        raise Fail("job_not_found", 404)
    return job


@endpoint()
def job(lang: str, name: str):
    lang = _lang(lang)
    j = _published_job(name)
    return _present(j, lang, _translations([j.name], lang).get(j.name), full=True)


# ---------------- accounts ----------------
@endpoint(methods=("POST",))
def signup(full_name: str, email: str, password: str, lang: str = "ar"):
    if not _settings().allow_signup:
        raise Fail("signup_closed", 403)
    full_name, email = (full_name or "").strip(), (email or "").strip().lower()
    if not (2 <= len(full_name) <= 140):
        raise Fail("name_required")
    if not validate_email_address(email):
        raise Fail("email_invalid")
    if not (8 <= len(password or "") <= 128):
        raise Fail("password_short")
    if frappe.db.exists("User", email):
        raise Fail("account_exists", 409)
    first, _, last = full_name.partition(" ")
    try:
        frappe.get_doc({"doctype": "User", "email": email, "first_name": first, "last_name": last,
                        "user_type": "Website User", "send_welcome_email": 0, "enabled": 1,
                        "language": lang if frappe.db.exists("Language", lang) else None,
                        "new_password": password}).insert(ignore_permissions=True)
    except frappe.exceptions.ValidationError as e:
        if "password" in str(e).lower():
            raise Fail("password_weak")
        raise
    return {"email": email, "full_name": full_name}


@endpoint(methods=("POST",))
def authenticate(email: str, password: str):
    """Checks an applicant's email and password. Staff accounts and the service user are refused."""
    from frappe.utils.password import check_password

    email = (email or "").strip().lower()
    user = frappe.db.get_value("User", email, ["name", "enabled", "user_type", "full_name"], as_dict=True) if email else None
    if not user or not user.enabled or user.user_type != "Website User" or SERVICE_ROLE in frappe.get_roles(user.name):
        raise Fail("bad_credentials", 401)
    try:
        check_password(user.name, password or "", delete_tracker_cache=False)
    except frappe.AuthenticationError:
        raise Fail("bad_credentials", 401)
    return {"email": user.name, "full_name": user.full_name or user.name}


def _applicant_user(email: str):
    email = (email or "").strip().lower()
    user = frappe.db.get_value("User", email, ["name", "enabled", "user_type", "full_name"], as_dict=True) if email else None
    if not user or not user.enabled or user.user_type != "Website User":
        raise Fail("signed_out", 401)
    return user


# ---------------- applications ----------------
@endpoint(methods=("POST",))
def apply(email: str, job: str, phone: str = "", country: str = "", cover_note: str = "", declaration: str = "0",
          lang: str = "ar"):
    user = _applicant_user(email)
    s = _settings()
    opening = _published_job(job)
    if not _is_open(opening):
        raise Fail("job_closed", 409)
    if str(declaration) not in ("1", "true", "on"):
        raise Fail("declaration_required")
    if not valid_phone(phone):
        raise Fail("phone_invalid")
    if frappe.db.exists("Job Applicant", {"email_id": user.name, "job_title": opening.name}):
        raise Fail("already_applied", 409)
    f = frappe.request.files.get("cv") if frappe.request else None
    if not f:
        raise Fail("cv_required")
    content = f.stream.read()
    max_mb = int(s.max_cv_mb or 5)
    try:
        ext = validate_cv(f.filename, content, max_mb)
    except ValueError as e:
        raise Fail({"size": "cv_too_large", "empty": "cv_empty"}.get(str(e), "cv_type"), max_mb=max_mb)

    doc = {"doctype": "Job Applicant", "applicant_name": (user.full_name or user.name)[:140], "email_id": user.name,
           "phone_number": phone.strip()[:30], "job_title": opening.name, "designation": opening.designation,
           "status": "Open", "cover_letter": frappe.utils.escape_html((cover_note or "").strip()[:4000]),
           "applied_via_portal": 1, "consent_ai_screening": 1, "consent_at": now_datetime(),
           "portal_language": (lang or "")[:10]}
    meta = frappe.get_meta("Job Applicant")
    if country and meta.has_field("country") and frappe.db.exists("Country", country):
        doc["country"] = country
    if meta.has_field("source") and frappe.db.exists("Job Applicant Source", "Website Listing"):
        doc["source"] = "Website Listing"
    applicant = frappe.get_doc(doc).insert(ignore_permissions=True)
    file_doc = frappe.get_doc({"doctype": "File", "file_name": f"cv-{frappe.scrub(user.full_name or 'cv')[:40] or 'cv'}.{ext}",
                               "content": content, "is_private": 1, "attached_to_doctype": "Job Applicant",
                               "attached_to_name": applicant.name, "attached_to_field": "resume_attachment"}
                              ).insert(ignore_permissions=True)
    frappe.db.set_value("Job Applicant", applicant.name, "resume_attachment", file_doc.file_url, update_modified=False)
    applicant.add_comment("Info", f"Applied via the careers portal ({lang}). Applicant declaration recorded.")
    return {"application": applicant.name, "reference": reference(applicant.name, applicant.creation)}


def _has_interview(name: str) -> bool:
    return bool(frappe.db.exists("Interview", {"job_applicant": name, "status": ["in", ["Pending", "Under Review"]]}))


def _job_titles(job_names: list, lang: str) -> dict:
    jobs_ = {j.name: j for j in frappe.get_all("Job Opening", filters={"name": ["in", job_names]},
                                                fields=["name", "job_title", "designation", "description"])} if job_names else {}
    tr = _translations(list(jobs_), lang)
    return {n: pick_translation(j.job_title or j.designation or n, "", tr.get(n))[0] for n, j in jobs_.items()}


@endpoint()
def applications(email: str, lang: str):
    lang = _lang(lang)
    user = _applicant_user(email)
    rows = frappe.get_all("Job Applicant", filters={"email_id": user.name},
                          fields=["name", "job_title", "status", "creation"], order_by="creation desc", limit_page_length=100)
    titles = _job_titles([r.job_title for r in rows if r.job_title], lang)
    return {"applications": [{"id": r.name, "job": r.job_title, "title": titles.get(r.job_title) or r.job_title or "",
                              "submitted": str(r.creation)[:10], "reference": reference(r.name, r.creation),
                              "status": candidate_status(r.status, _has_interview(r.name))} for r in rows]}


def _own(email: str, name: str):
    user = _applicant_user(email)
    a = frappe.db.get_value("Job Applicant", name, ["name", "email_id", "job_title", "status", "creation"], as_dict=True) \
        if name else None
    if not a or (a.email_id or "").lower() != user.name:
        raise Fail("application_not_found", 404)
    return a


@endpoint()
def application(email: str, name: str, lang: str):
    lang = _lang(lang)
    a = _own(email, name)
    extra = frappe.db.get_value("Job Applicant", a.name, ["ai_status", "ai_profile"], as_dict=True) \
        if frappe.get_meta("Job Applicant").has_field("ai_status") else {}
    state = (extra or {}).get("ai_status")
    out = {"id": a.name, "job": a.job_title, "title": _job_titles([a.job_title], lang).get(a.job_title, ""),
           "submitted": str(a.creation)[:10], "reference": reference(a.name, a.creation),
           "status": candidate_status(a.status, _has_interview(a.name)),
           "details_state": "ready" if (extra or {}).get("ai_profile") else ("unavailable" if state == "Failed" else "preparing"),
           "details": _details((extra or {}).get("ai_profile"))}
    return out


def _details(raw):
    if not raw:
        return None
    try:
        p = json.loads(raw)
    except ValueError:
        return None
    return {
        "years": p.get("years_of_experience"),
        "work": [{"position": w.get("position", ""), "employer": w.get("name") or w.get("description") or "",
                  "start": w.get("startDate", ""), "end": w.get("endDate", "")} for w in p.get("work") or []][:10],
        "education": [{"degree": " ".join(x for x in (e.get("studyType"), e.get("area")) if x),
                       "institution": e.get("institution", ""), "end": e.get("endDate", "")}
                      for e in p.get("education") or []][:6],
        "skills": list(dict.fromkeys(k for s in p.get("skills") or [] for k in (s.get("keywords") or [s.get("name")]) if k))[:30],
        "languages": [l.get("language") + (f" ({l.get('fluency')})" if l.get("fluency") else "")
                      for l in p.get("languages") or [] if l.get("language")][:8],
        "certificates": [c.get("name") for c in p.get("certificates") or [] if c.get("name")][:10],
    }


@endpoint(methods=("POST",))
def add_correction(email: str, name: str, note: str):
    a = _own(email, name)
    note = (note or "").strip()
    if not (3 <= len(note) <= 2000):
        raise Fail("note_length")
    doc = frappe.get_doc("Job Applicant", a.name)
    doc.add_comment("Comment", "Applicant correction to their CV details:<br>" + frappe.utils.escape_html(note),
                    comment_email=a.email_id, comment_by=frappe.utils.get_fullname(a.email_id))
    return {"ok": True}


# ---------------- files ----------------
@endpoint()
def logo(dark: int = 0):
    """Streams the configured logo so the portal can show it without reaching Frappe from the browser."""
    s = _settings()
    url = (s.logo_dark if int(dark or 0) else s.logo) or s.logo
    if not url:
        raise Fail("no_logo", 404)
    f = frappe.get_all("File", filters={"file_url": url}, fields=["name"], limit=1)
    if not f:
        raise Fail("no_logo", 404)
    file_doc = frappe.get_doc("File", f[0].name)
    frappe.local.response.filename = file_doc.file_name
    frappe.local.response.filecontent = file_doc.get_content()
    frappe.local.response.type = "download"
    frappe.local.response.display_content_as = "inline"
