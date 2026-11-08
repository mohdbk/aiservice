"""Shared context and queries for the careers portal pages."""
import frappe
from frappe.utils import getdate, today
from frappe.utils.html_utils import clean_html

from moet_careers import __version__
from moet_careers.portal_utils import clean_department, localized, pick_lang, text

SETTINGS = "MoET Careers Settings"
PAGE_SIZE = 10


def base_context(context, title_en: str = "", title_ar: str = ""):
    """Language, direction, branding and helpers for every portal page."""
    lang = pick_lang(frappe.form_dict.get("lang"), frappe.request.cookies.get("moet_lang") if frappe.request else None)
    if frappe.form_dict.get("lang") in ("ar", "en") and getattr(frappe.local, "cookie_manager", None):
        frappe.local.cookie_manager.set_cookie("moet_lang", lang, max_age=365 * 24 * 3600, httponly=True)
    frappe.local.lang = lang

    s = frappe.get_cached_doc(SETTINGS)
    context.no_cache = 1
    context.lang = lang
    context.dir = "rtl" if lang == "ar" else "ltr"
    context.t = lambda en, ar: text(lang, en, ar)
    context.settings = s
    context.entity_name = text(lang, s.name_en or "Ministry of Economy & Tourism", s.name_ar or s.name_en or "")
    context.portal_title = text(lang, s.portal_title_en or "Careers", s.portal_title_ar or "")
    context.page_title = text(lang, title_en, title_ar) if (title_en or title_ar) else context.portal_title
    context.user = frappe.session.user
    context.is_guest = frappe.session.user == "Guest"
    context.user_name = "" if context.is_guest else (frappe.utils.get_fullname(frappe.session.user) or "")
    context.csrf_token = frappe.sessions.get_csrf_token() if not context.is_guest else ""
    context.asset_version = __version__
    context.year = today()[:4]
    path = frappe.request.path if frappe.request else "/careers"
    query = {k: v for k, v in frappe.form_dict.items() if k not in ("lang", "cmd") and isinstance(v, str)}
    context.current_path = path
    context.other_lang = "en" if lang == "ar" else "ar"
    context.other_lang_url = path + "?" + frappe.utils.urlencode({**query, "lang": context.other_lang})
    return context


def require_login(context):
    if frappe.session.user == "Guest":
        target = frappe.request.full_path if frappe.request else "/careers"
        frappe.local.flags.redirect_location = "/careers/login?" + frappe.utils.urlencode({"redirect-to": target})
        raise frappe.Redirect


def _has(doctype, field):
    return frappe.get_meta(doctype).has_field(field)


def open_jobs_filters():
    return [["publish", "=", 1], ["status", "=", "Open"]]


def job_fields():
    fields = ["name", "job_title", "designation", "department", "closes_on", "posted_on", "description"]
    for f in ("job_title_ar", "description_ar", "location", "employment_type", "lower_range", "upper_range",
              "currency", "publish_salary_range", "salary_per"):
        if _has("Job Opening", f):
            fields.append(f)
    return fields


def is_open(job) -> bool:
    return not job.get("closes_on") or getdate(job.closes_on) >= getdate(today())


def present_job(job, lang):
    j = frappe._dict(job)
    j.title = localized(lang, j.job_title, j.get("job_title_ar")) or j.designation or j.name
    j.department_label = clean_department(j.department)
    desc = localized(lang, j.description, j.get("description_ar"))
    j.description_html = clean_html(desc or "")
    j.summary = frappe.utils.strip_html_tags(desc or "")[:220]
    j.closes_label = frappe.utils.formatdate(j.closes_on, "dd/MM/yyyy") if j.closes_on else ""
    j.salary = ""
    if j.get("publish_salary_range") and (j.get("lower_range") or j.get("upper_range")):
        lo, hi = j.get("lower_range") or 0, j.get("upper_range") or 0
        cur = j.get("currency") or "AED"
        j.salary = f"{cur} {lo:,.0f} - {hi:,.0f}" if lo and hi else f"{cur} {(lo or hi):,.0f}"
    return j


def search_jobs(lang, q="", department="", location="", employment_type="", page=1):
    filters = open_jobs_filters()
    if department:
        filters.append(["department", "=", department])
    if location and _has("Job Opening", "location"):
        filters.append(["location", "=", location])
    if employment_type and _has("Job Opening", "employment_type"):
        filters.append(["employment_type", "=", employment_type])
    or_filters = None
    if q:
        like = f"%{q.strip()[:100]}%"
        or_filters = [["job_title", "like", like], ["designation", "like", like], ["description", "like", like]]
        if _has("Job Opening", "job_title_ar"):
            or_filters += [["job_title_ar", "like", like], ["description_ar", "like", like]]
    rows = frappe.get_all("Job Opening", fields=job_fields(), filters=filters, or_filters=or_filters,
                          order_by="posted_on desc, creation desc", limit_page_length=500)
    rows = [r for r in rows if is_open(r)]
    total = len(rows)
    page = max(1, int(page or 1))
    start = (page - 1) * PAGE_SIZE
    return [present_job(r, lang) for r in rows[start:start + PAGE_SIZE]], total


def filter_options():
    rows = frappe.get_all("Job Opening", fields=["department"] + (["location"] if _has("Job Opening", "location") else [])
                          + (["employment_type"] if _has("Job Opening", "employment_type") else []),
                          filters=open_jobs_filters(), limit_page_length=500)
    opts = {"department": set(), "location": set(), "employment_type": set()}
    for r in rows:
        for k in opts:
            if r.get(k):
                opts[k].add(r[k])
    return {k: sorted(v) for k, v in opts.items()}


def get_open_job(name, lang):
    if not name or not frappe.db.exists("Job Opening", name):
        raise frappe.DoesNotExistError
    job = frappe.db.get_value("Job Opening", name, job_fields(), as_dict=True)
    published = frappe.db.get_value("Job Opening", name, ["publish", "status"], as_dict=True)
    if not published.publish:
        raise frappe.DoesNotExistError
    j = present_job(job, lang)
    j.accepting = published.status == "Open" and is_open(job)
    return j
