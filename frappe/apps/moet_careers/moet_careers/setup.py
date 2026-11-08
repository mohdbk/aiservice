import json
import os

import frappe
from frappe.custom.doctype.custom_field.custom_field import create_custom_fields

SETTINGS = "MoET Careers Settings"
SERVICE_ROLE = "Careers Portal Service"
SERVICE_USER = "careers-portal@service.invalid"

CUSTOM_FIELDS = {
    "Job Opening": [
        # Own tabs at the end of the form, so they are visible whatever the form's saved field order is.
        # Arabic is the original (main fields); the English goes in its own tab.
        {"fieldname": "portal_translations_tab", "fieldtype": "Tab Break", "label": "English",
         "insert_after": "column_break_pfir"},
        {"fieldname": "job_title_en", "fieldtype": "Data", "label": "Job Title (English)",
         "insert_after": "portal_translations_tab",
         "description": "Write the job in Arabic in the main fields and in English here. Portal > Suggest English drafts it for you."},
        {"fieldname": "description_en", "fieldtype": "Text Editor", "label": "Description (English)",
         "insert_after": "job_title_en"},
        {"fieldname": "english_approved", "fieldtype": "Check", "label": "English Approved", "insert_after": "description_en",
         "description": "Only approved English is shown on the English portal. Writing or editing the English yourself approves it."},
        {"fieldname": "portal_translations", "fieldtype": "Table", "options": "Job Opening Translation", "hidden": 1,
         "label": "Translations", "insert_after": "english_approved",
         "description": "Stored translations, one row per language. Filled from the English fields."},
        {"fieldname": "assessment_stages_tab", "fieldtype": "Tab Break", "label": "Assessments",
         "insert_after": "portal_translations"},
        {"fieldname": "assessment_stages", "fieldtype": "Table", "options": "Job Assessment Stage", "label": "Assessments",
         "insert_after": "assessment_stages_tab",
         "description": "Tests for this job, in order. 'When They Apply' tests are sent automatically to every new applicant."},
        {"fieldname": "screening_section", "fieldtype": "Section Break", "label": "Screening Questions",
         "insert_after": "assessment_stages"},
        {"fieldname": "screening_questions", "fieldtype": "Table", "options": "Job Screening Question",
         "label": "Screening Questions", "insert_after": "screening_section",
         "description": "Asked on the application form, in this order."},
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
        {"fieldname": "assessment_summary", "fieldtype": "Small Text", "label": "Assessments", "read_only": 1,
         "insert_after": "portal_language"},
        {"fieldname": "screening_result", "fieldtype": "Select", "label": "Screening",
         "options": "\nMeets Requirements\nDoes Not Meet Requirements\nNo Requirements", "read_only": 1,
         "insert_after": "assessment_summary", "in_standard_filter": 1, "in_list_view": 1},
        {"fieldname": "screening_summary", "fieldtype": "Small Text", "label": "Screening Answers", "read_only": 1,
         "insert_after": "screening_result"},
        {"fieldname": "screening_answers", "fieldtype": "Long Text", "label": "Screening Answers (Data)", "read_only": 1,
         "hidden": 1, "insert_after": "screening_summary"},
    ],
}


OLD_FIELDS = ("Job Opening-portal_translations_section", "Job Opening-assessment_stages_section")


def after_migrate():
    for name in OLD_FIELDS:
        if frappe.db.exists("Custom Field", name):
            frappe.delete_doc("Custom Field", name, ignore_permissions=True, force=True)
    create_custom_fields(CUSTOM_FIELDS, ignore_validate=True, update=True)
    _fix_field_order("Job Opening", ["portal_translations_tab", "job_title_en", "description_en", "english_approved",
                                     "portal_translations", "assessment_stages_tab", "assessment_stages",
                                     "screening_section", "screening_questions"])
    frappe.clear_cache(doctype="Job Opening")
    _settings_defaults()
    _backfill_verified()
    ensure_service_user()
    _safely("Arabic original", "moet_careers.bilingual.backfill")
    _safely("Recruitment desk setup", "moet_careers.desk_setup.setup")
    frappe.db.commit()


def _safely(title: str, method: str):
    """Runs an optional setup step; a failure is logged and never stops the migration."""
    try:
        frappe.get_attr(method)()
        frappe.db.commit()
    except Exception:
        frappe.db.rollback()
        frappe.log_error(frappe.get_traceback(), title)


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


def _fix_field_order(doctype: str, fields: list):
    """If the form's field order was saved in Customize Form, put our fields at its end in the right order."""
    ps = frappe.db.get_value("Property Setter", {"doc_type": doctype, "property": "field_order"}, ["name", "value"], as_dict=True)
    if not ps:
        return
    try:
        order = [f for f in json.loads(ps.value) if f not in fields] + fields
    except ValueError:
        return
    frappe.db.set_value("Property Setter", ps.name, "value", json.dumps(order), update_modified=False)


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
