import frappe

from moet_careers.portal import base_context, get_open_job, require_login

no_cache = 1


def get_context(context):
    base_context(context, "Apply", "التقديم")
    require_login(context)
    try:
        context.job = get_open_job(frappe.form_dict.get("job"), context.lang)
    except frappe.DoesNotExistError:
        frappe.local.flags.redirect_location = "/careers"
        raise frappe.Redirect
    context.email = (frappe.db.get_value("User", frappe.session.user, "email") or "").lower()
    existing = frappe.db.get_value("Job Applicant", {"email_id": context.email, "job_title": context.job.name}, "name")
    if existing or not context.job.accepting:
        frappe.local.flags.redirect_location = f"/careers/applications/{existing}" if existing else f"/careers/jobs/{context.job.name}"
        raise frappe.Redirect
    context.countries = frappe.get_all("Country", pluck="name", order_by="name asc", limit_page_length=300)
    context.page_title = context.job.title
    return context
