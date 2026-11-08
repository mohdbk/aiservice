import math

import frappe

from moet_careers.portal import PAGE_SIZE, base_context, filter_options, search_jobs
from moet_careers.portal_utils import clean_department

no_cache = 1


def get_context(context):
    base_context(context, "Careers", "الوظائف")
    f = frappe.form_dict
    context.q = (f.get("q") or "").strip()[:100]
    context.department = f.get("department") or ""
    context.location = f.get("location") or ""
    page = int(f.get("page") or 1) if str(f.get("page") or "1").isdigit() else 1
    context.jobs, context.total = search_jobs(context.lang, context.q, context.department, context.location,
                                              page=page)
    context.page = page
    context.pages = max(1, math.ceil(context.total / PAGE_SIZE))
    context.options = filter_options()
    context.clean_department = clean_department

    def page_url(p):
        params = {k: v for k, v in {"q": context.q, "department": context.department, "location": context.location,
                                    "page": p, "lang": context.lang}.items() if v}
        return "/careers?" + frappe.utils.urlencode(params)

    context.page_url = page_url
    return context
