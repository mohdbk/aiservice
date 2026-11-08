import frappe

from moet_careers.api import _own_applicant, has_interview, job_title_for
from moet_careers.portal import base_context, require_login
from moet_careers.portal_utils import candidate_status, reference

no_cache = 1


def get_context(context):
    base_context(context, "Application", "الطلب")
    require_login(context)
    try:
        a = _own_applicant(frappe.form_dict.get("application"))
    except frappe.PermissionError:
        frappe.local.flags.redirect_location = "/careers/applications"
        raise frappe.Redirect
    context.name = a.name
    context.title = job_title_for(a.job_title, context.lang) if a.job_title else ""
    context.page_title = context.title
    context.submitted = frappe.utils.formatdate(a.creation, "dd/MM/yyyy")
    context.reference = reference(a.name, a.creation)
    context.status = candidate_status(a.status, has_interview(a.name))
    context.just_applied = frappe.form_dict.get("new") == "1"
    return context
