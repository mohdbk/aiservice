"""0.2: job translations move from the Arabic-only custom fields to the Portal Translations table."""
import frappe


def execute():
    cols = frappe.db.get_table_columns("Job Opening")
    if "job_title_ar" in cols:
        rows = frappe.db.sql("""select name, job_title_ar, description_ar from `tabJob Opening`
                                where ifnull(job_title_ar, '') != '' or ifnull(description_ar, '') != ''""", as_dict=True)
        for r in rows:
            if frappe.db.exists("Job Opening Translation", {"parent": r.name, "language": "ar"}):
                continue
            idx = (frappe.db.sql("select max(idx) from `tabJob Opening Translation` where parent=%s", r.name)[0][0] or 0) + 1
            frappe.get_doc({"doctype": "Job Opening Translation", "parent": r.name, "parenttype": "Job Opening",
                            "parentfield": "portal_translations", "idx": idx, "language": "ar", "status": "Approved",
                            "job_title": r.job_title_ar, "description": r.description_ar,
                            "approved_by": "Administrator"}).db_insert()
    for field in ("job_title_ar", "description_ar"):
        name = f"Job Opening-{field}"
        if frappe.db.exists("Custom Field", name):
            frappe.delete_doc("Custom Field", name, ignore_permissions=True, force=True)
