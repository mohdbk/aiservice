import frappe
from frappe.custom.doctype.custom_field.custom_field import create_custom_fields

SETTINGS = "MoET Careers Settings"

CUSTOM_FIELDS = {
    "Job Opening": [
        {"fieldname": "job_title_ar", "fieldtype": "Data", "label": "Job Title (Arabic)", "insert_after": "job_title",
         "translatable": 0},
        {"fieldname": "description_ar", "fieldtype": "Text Editor", "label": "Job Description (Arabic)",
         "insert_after": "description"},
    ],
    "Job Applicant": [
        {"fieldname": "portal_section", "fieldtype": "Section Break", "label": "Careers Portal",
         "insert_after": "cover_letter", "collapsible": 1},
        {"fieldname": "applied_via_portal", "fieldtype": "Check", "label": "Applied via Careers Portal", "read_only": 1,
         "insert_after": "portal_section"},
        {"fieldname": "consent_ai_screening", "fieldtype": "Check", "label": "Consented to AI-assisted Screening",
         "read_only": 1, "insert_after": "applied_via_portal"},
        {"fieldname": "consent_at", "fieldtype": "Datetime", "label": "Consent Given On", "read_only": 1,
         "insert_after": "consent_ai_screening"},
        {"fieldname": "portal_language", "fieldtype": "Data", "label": "Portal Language", "read_only": 1,
         "insert_after": "consent_at"},
    ],
}


def after_migrate():
    create_custom_fields(CUSTOM_FIELDS, ignore_validate=True, update=True)
    _settings_defaults()
    frappe.db.commit()


def _settings_defaults():
    saved = {r[0] for r in frappe.db.sql("select field from `tabSingles` where doctype=%s", SETTINGS)}
    for df in frappe.get_meta(SETTINGS).fields:
        if df.default and df.fieldname not in saved and df.fieldtype not in ("Section Break", "Column Break"):
            frappe.db.set_single_value(SETTINGS, df.fieldname, df.default)
    frappe.clear_document_cache(SETTINGS, SETTINGS)
