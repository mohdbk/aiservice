import frappe
from frappe.custom.doctype.custom_field.custom_field import create_custom_fields

SETTINGS = "AI Recruitment Settings"

CUSTOM_FIELDS = {
    "Job Applicant": [
        {"fieldname": "ai_section", "fieldtype": "Section Break", "label": "AI Screening", "insert_after": None},
        {"fieldname": "ai_score", "fieldtype": "Float", "label": "AI Score", "precision": "1", "read_only": 1,
         "in_list_view": 1, "in_standard_filter": 1, "insert_after": "ai_section"},
        {"fieldname": "ai_status", "fieldtype": "Select", "label": "AI Status",
         "options": "\nPending\nScored\nFailed\nSkipped", "read_only": 1, "in_standard_filter": 1,
         "insert_after": "ai_score"},
        {"fieldname": "ai_col", "fieldtype": "Column Break", "insert_after": "ai_status"},
        {"fieldname": "ai_model", "fieldtype": "Data", "label": "AI Model", "read_only": 1, "insert_after": "ai_col"},
        {"fieldname": "ai_scored_on", "fieldtype": "Datetime", "label": "Scored On", "read_only": 1,
         "insert_after": "ai_model"},
        {"fieldname": "ai_detail_section", "fieldtype": "Section Break", "insert_after": "ai_scored_on"},
        {"fieldname": "ai_summary", "fieldtype": "Small Text", "label": "AI Summary", "read_only": 1,
         "insert_after": "ai_detail_section"},
        {"fieldname": "ai_must_have_gaps", "fieldtype": "Small Text", "label": "Must-have Gaps", "read_only": 1,
         "insert_after": "ai_summary"},
        {"fieldname": "ai_reasoning", "fieldtype": "Long Text", "label": "Per-criterion Reasoning", "read_only": 1,
         "insert_after": "ai_must_have_gaps"},
        {"fieldname": "ai_profile_section", "fieldtype": "Section Break", "label": "AI Parsed CV",
         "collapsible": 1, "insert_after": "ai_reasoning"},
        {"fieldname": "ai_years_experience", "fieldtype": "Float", "label": "Years of Experience (parsed)",
         "precision": "1", "read_only": 1, "in_standard_filter": 1, "insert_after": "ai_profile_section"},
        {"fieldname": "ai_cv_language", "fieldtype": "Data", "label": "CV Language", "read_only": 1,
         "insert_after": "ai_years_experience"},
        {"fieldname": "ai_profile_col", "fieldtype": "Column Break", "insert_after": "ai_cv_language"},
        {"fieldname": "ai_cv_read", "fieldtype": "Data", "label": "CV Read As", "read_only": 1,
         "description": "Layout detected / reading method", "insert_after": "ai_profile_col"},
        {"fieldname": "ai_profile_json_section", "fieldtype": "Section Break", "insert_after": "ai_cv_read",
         "collapsible": 1, "label": "Structured Profile (JSON Resume)"},
        {"fieldname": "ai_profile", "fieldtype": "Code", "options": "JSON", "label": "Structured Profile",
         "read_only": 1, "insert_after": "ai_profile_json_section"},
    ],
    "Job Opening": [
        {"fieldname": "ai_scoring_criteria", "fieldtype": "Long Text", "label": "AI Scoring Criteria",
         "insert_after": "description",
         "description": "One criterion per line: weight | criterion | must (optional). "
                        "Example: 30 | 3+ years building production ML systems | must"},
    ],
}


def after_migrate():
    fields = {dt: [{k: v for k, v in f.items() if v is not None} for f in fl] for dt, fl in CUSTOM_FIELDS.items()}
    create_custom_fields(fields, ignore_validate=True, update=True)
    _ensure_settings_defaults()
    frappe.db.commit()


def _ensure_settings_defaults():
    """Fill defaults for any setting that has never been saved (new fields on upgrade
    included), without touching values an admin has set."""
    saved = {r[0] for r in frappe.db.sql("select field from `tabSingles` where doctype=%s", SETTINGS)}
    for df in frappe.get_meta(SETTINGS).fields:
        if df.default and df.fieldname not in saved and df.fieldtype not in ("Section Break", "Column Break", "HTML"):
            frappe.db.set_single_value(SETTINGS, df.fieldname, df.default)
    frappe.clear_document_cache(SETTINGS, SETTINGS)


def setting(fieldname, fallback):
    """Read a setting, treating empty/0 as 'use the default'."""
    return frappe.get_cached_doc(SETTINGS).get(fieldname) or fallback


def allowed_roles() -> set[str]:
    s = frappe.get_cached_doc(SETTINGS)
    return {r.strip() for r in (s.chat_roles or "").splitlines() if r.strip()}


def chat_allowed(user: str | None = None) -> bool:
    s = frappe.get_cached_doc(SETTINGS)
    if not (s.enabled and s.chat_enabled):
        return False
    return bool(allowed_roles() & set(frappe.get_roles(user)))


def boot(bootinfo):
    try:
        bootinfo.ai_recruitment = {"chat": chat_allowed()}
    except Exception:
        bootinfo.ai_recruitment = {"chat": False}
