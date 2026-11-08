import frappe

from moet_careers.api import has_interview, job_title_for
from moet_careers.portal import base_context, require_login
from moet_careers.portal_utils import candidate_status, reference

no_cache = 1


def get_context(context):
    base_context(context, "My applications", "طلباتي")
    require_login(context)
    email = (frappe.db.get_value("User", frappe.session.user, "email") or "").lower()
    rows = frappe.get_all("Job Applicant", filters={"email_id": email},
                          fields=["name", "job_title", "status", "creation"], order_by="creation desc",
                          limit_page_length=100)
    context.applications = [frappe._dict(
        name=r.name, title=job_title_for(r.job_title, context.lang) if r.job_title else "",
        submitted=frappe.utils.formatdate(r.creation, "dd/MM/yyyy"),
        reference=reference(r.name, r.creation),
        status=candidate_status(r.status, has_interview(r.name)),
    ) for r in rows]
    return context
