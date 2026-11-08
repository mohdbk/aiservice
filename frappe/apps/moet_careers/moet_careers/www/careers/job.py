import frappe

from moet_careers.portal import base_context, get_open_job

no_cache = 1


def get_context(context):
    base_context(context)
    try:
        context.job = get_open_job(frappe.form_dict.get("job"), context.lang)
    except frappe.DoesNotExistError:
        frappe.local.flags.redirect_location = "/careers"
        raise frappe.Redirect
    context.page_title = context.job.title
    context.applied = None
    if not context.is_guest:
        email = (frappe.db.get_value("User", frappe.session.user, "email") or "").lower()
        context.applied = frappe.db.get_value("Job Applicant", {"email_id": email, "job_title": context.job.name}, "name")
    return context
