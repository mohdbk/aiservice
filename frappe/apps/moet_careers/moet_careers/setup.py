import os

import frappe
from frappe.custom.doctype.custom_field.custom_field import create_custom_fields

SETTINGS = "MoET Careers Settings"
SERVICE_ROLE = "Careers Portal Service"
SERVICE_USER = "careers-portal@service.invalid"

CUSTOM_FIELDS = {
    "Job Opening": [
        {"fieldname": "portal_translations_section", "fieldtype": "Section Break", "label": "Portal Translations",
         "insert_after": "description", "collapsible": 1},
        {"fieldname": "portal_translations", "fieldtype": "Table", "options": "Job Opening Translation",
         "label": "Translations", "insert_after": "portal_translations_section",
         "description": "One row per portal language. Use Suggest Translations for drafts; only Approved rows are shown "
                        "on the portal, otherwise the job is shown in its original language."},
    ],
    "User": [
        {"fieldname": "portal_account_section", "fieldtype": "Section Break", "label": "Careers Portal",
         "insert_after": "user_type", "collapsible": 1},
        {"fieldname": "portal_email_verified", "fieldtype": "Check", "label": "Portal Email Verified", "read_only": 1,
         "insert_after": "portal_account_section"},
        {"fieldname": "portal_session_epoch", "fieldtype": "Int", "label": "Portal Session Version", "read_only": 1,
         "insert_after": "portal_email_verified",
         "description": "Raised by a password reset; signs the applicant out of every portal session."},
    ],
    "Job Applicant": [
        {"fieldname": "portal_section", "fieldtype": "Section Break", "label": "Careers Portal",
         "insert_after": "cover_letter", "collapsible": 1},
        {"fieldname": "applied_via_portal", "fieldtype": "Check", "label": "Applied via Careers Portal", "read_only": 1,
         "insert_after": "portal_section"},
        {"fieldname": "consent_ai_screening", "fieldtype": "Check", "label": "Applicant Declaration Confirmed",
         "read_only": 1, "insert_after": "applied_via_portal"},
        {"fieldname": "consent_at", "fieldtype": "Datetime", "label": "Declaration Given On", "read_only": 1,
         "insert_after": "consent_ai_screening"},
        {"fieldname": "portal_language", "fieldtype": "Data", "label": "Portal Language", "read_only": 1,
         "insert_after": "consent_at"},
    ],
}


def after_migrate():
    create_custom_fields(CUSTOM_FIELDS, ignore_validate=True, update=True)
    _settings_defaults()
    _backfill_verified()
    ensure_service_user()
    frappe.db.commit()


def _settings_defaults():
    saved = {r[0] for r in frappe.db.sql("select field from `tabSingles` where doctype=%s", SETTINGS)}
    for df in frappe.get_meta(SETTINGS).fields:
        if df.default and df.fieldname not in saved and df.fieldtype not in ("Section Break", "Column Break", "Table"):
            frappe.db.set_single_value(SETTINGS, df.fieldname, df.default)
    frappe.clear_document_cache(SETTINGS, SETTINGS)
    s = frappe.get_doc(SETTINGS)
    env_url = os.environ.get("CAREERS_PORTAL_URL", "").strip().rstrip("/")
    if env_url and not s.portal_url:
        s.portal_url = env_url
        s.flags.ignore_permissions = True
        s.save()
    if not s.get("languages"):
        for code in ("ar", "en"):
            if frappe.db.exists("Language", code):
                s.append("languages", {"language": code, "enabled": 1})
        s.flags.ignore_permissions = True
        s.save()


def _backfill_verified():
    """Accounts created before email verification existed count as verified (runs once)."""
    if frappe.db.get_default("moet_careers_verified_backfill"):
        return
    frappe.db.sql("update `tabUser` set portal_email_verified=1 where user_type='Website User'")
    frappe.db.set_default("moet_careers_verified_backfill", "1")


def ensure_service_user():
    """Service account the portal backend uses. Its API key and secret come from the environment
    (CAREERS_PORTAL_API_KEY / CAREERS_PORTAL_API_SECRET) or are generated in Desk (User > API Access)."""
    if not frappe.db.exists("Role", SERVICE_ROLE):
        frappe.get_doc({"doctype": "Role", "role_name": SERVICE_ROLE, "desk_access": 0}).insert(ignore_permissions=True)
    if not frappe.db.exists("User", SERVICE_USER):
        user = frappe.get_doc({"doctype": "User", "email": SERVICE_USER, "first_name": "Careers Portal",
                               "user_type": "Website User", "send_welcome_email": 0, "enabled": 1})
        user.append("roles", {"role": SERVICE_ROLE})
        user.flags.ignore_password_policy = True
        user.insert(ignore_permissions=True)
    user = frappe.get_doc("User", SERVICE_USER)
    if SERVICE_ROLE not in [r.role for r in user.roles]:
        user.append("roles", {"role": SERVICE_ROLE})
    key, secret = os.environ.get("CAREERS_PORTAL_API_KEY", "").strip(), os.environ.get("CAREERS_PORTAL_API_SECRET", "").strip()
    if key and secret:
        user.api_key = key
        user.api_secret = secret
    user.flags.ignore_permissions = True
    user.save()
