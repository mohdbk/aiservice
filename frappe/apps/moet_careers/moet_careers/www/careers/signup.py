import frappe

from moet_careers.portal import base_context
from moet_careers.portal_utils import safe_redirect

no_cache = 1


def get_context(context):
    base_context(context, "Create account", "إنشاء حساب")
    context.redirect_to = safe_redirect(frappe.form_dict.get("redirect-to"))
    if not context.is_guest:
        frappe.local.flags.redirect_location = context.redirect_to
        raise frappe.Redirect
    return context
