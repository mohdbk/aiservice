"""House style: no em dashes anywhere. Cleans job titles and descriptions created before the rule."""
import re

import frappe

DASH = "\u2014"


def execute():
    rows = frappe.db.sql(f"select name, job_title, description from `tabJob Opening` "
                         f"where job_title like %s or description like %s", (f"%{DASH}%", f"%{DASH}%"), as_dict=True)
    for r in rows:
        title = re.sub(rf"\s*{DASH}\s*", ": ", r.job_title or "")
        desc = re.sub(rf"\s*{DASH}\s*", ", ", r.description or "")
        frappe.db.set_value("Job Opening", r.name, {"job_title": title, "description": desc}, update_modified=False)
    t = frappe.db.sql(f"select name, job_title, description from `tabJob Opening Translation` "
                      f"where job_title like %s or description like %s", (f"%{DASH}%", f"%{DASH}%"), as_dict=True)
    for r in t:
        frappe.db.set_value("Job Opening Translation", r.name,
                            {"job_title": re.sub(rf"\s*{DASH}\s*", ": ", r.job_title or ""),
                             "description": re.sub(rf"\s*{DASH}\s*", ", ", r.description or "")}, update_modified=False)
