"""Recruitment-only Desk for HR staff.

Two roles see only recruitment: Recruiter (day-to-day) and Recruitment Admin (also portal wording and settings).
They get permissions on the recruitment doctypes, read or select access to what those forms link to, and the
"Careers" workspace as their home page. Nothing else in ERPNext or Frappe HR is granted, so it stays hidden.
System Managers and the Administrator are unaffected. Runs on every migrate (idempotent).
"""
import json

import frappe
from frappe.permissions import add_permission, update_permission_property

RECRUITER = "Recruiter"
ADMIN = "Recruitment Admin"
WORKSPACE = "Careers"

FULL = ("read", "write", "create", "report", "export", "print", "email", "share")

# doctype: (recruiter rights, admin rights). Rights not listed are off.
DOCTYPES = {
    # Frappe HR recruitment
    "Job Opening": (FULL, FULL + ("delete",)),
    "Job Applicant": (FULL, FULL + ("delete",)),
    "Job Applicant Source": (("read", "select"), FULL),
    "Interview": (FULL, FULL + ("delete",)),
    "Interview Round": (("read", "report"), FULL + ("delete",)),
    "Interview Type": (("read",), FULL),
    "Interview Feedback": (FULL, FULL + ("delete",)),
    "Job Offer": (FULL, FULL + ("delete",)),
    "Job Offer Term Template": (("read",), FULL),
    "Offer Term": (("read",), FULL),
    "Job Opening Template": (("read",), FULL),
    # MoET careers portal
    "Assessment": (FULL, FULL + ("delete",)),
    "Assessment Question": (FULL, FULL + ("delete",)),
    "Assessment Invitation": (FULL, FULL + ("delete",)),
    "Assessment Attempt": (("read", "write", "report", "export", "print"), ("read", "write", "report", "export", "print", "delete")),
    "Screening Question": (FULL, FULL + ("delete",)),
    "Portal Text": (("read", "report"), FULL + ("delete",)),
    "MoET Careers Settings": ((), ("read", "write")),
    # AI recruitment (back office)
    "AI Recruitment Settings": ((), ("read", "write")),
    "AI Chat Log": ((), ("read", "report")),
    # Read-only masters the recruitment forms link to
    "Designation": (("read", "select"), ("read", "select", "create", "write")),
    "Department": (("read", "select"), ("read", "select")),
    "Company": (("read", "select"), ("read", "select")),
    "Branch": (("read", "select"), ("read", "select")),
    "Location": (("read", "select"), ("read", "select")),
    "Employment Type": (("read", "select"), ("read", "select", "create", "write")),
    "Currency": (("read", "select"), ("read", "select")),
    "Country": (("read", "select"), ("read", "select")),
    "Language": (("read", "select"), ("read", "select")),
    "Employee": (("select",), ("select",)),
    "User": (("select",), ("select",)),
    "Email Template": (("read", "select"), FULL),
}

SECTIONS = [
    ("Hiring", ["Job Opening", "Job Applicant", "Interview", "Interview Feedback", "Job Offer"]),
    ("Assessments", ["Assessment Invitation", "Assessment Attempt", "Assessment", "Assessment Question"]),
    ("Screening", ["Screening Question"]),
    ("Setup", ["Interview Round", "Job Opening Template", "Job Applicant Source", "Designation", "Employment Type"]),
    ("Portal", ["Portal Text", "MoET Careers Settings"]),
    ("Assistant", ["AI Recruitment Settings", "AI Chat Log"]),
]
SHORTCUTS = [("Job Opening", "Open Jobs", {"status": "Open"}), ("Job Applicant", "New Applicants", {"status": "Open"}),
             ("Interview", "Upcoming Interviews", {"status": "Pending"}),
             ("Assessment Attempt", "To Mark", {"status": "Submitted"})]


def setup():
    for role in (RECRUITER, ADMIN):
        if not frappe.db.exists("Role", role):
            frappe.get_doc({"doctype": "Role", "role_name": role, "desk_access": 1}).insert(ignore_permissions=True)
        frappe.db.set_value("Role", role, {"desk_access": 1, "home_page": f"app/{WORKSPACE.lower()}"}, update_modified=False)
    for doctype, (rec, adm) in DOCTYPES.items():
        if not frappe.db.exists("DocType", doctype):
            continue
        for role, rights in ((RECRUITER, rec), (ADMIN, adm)):
            if rights:
                _grant(doctype, role, rights)
    _workspace()
    frappe.clear_cache()


def _grant(doctype: str, role: str, rights: tuple):
    """Adds a permission row for the role (level 0) and sets exactly the given rights on it."""
    if not frappe.db.exists("Custom DocPerm", {"parent": doctype, "role": role, "permlevel": 0}):
        add_permission(doctype, role, 0)
    for right in ("read", "write", "create", "delete", "report", "export", "print", "email", "share", "select"):
        update_permission_property(doctype, role, 0, right, 1 if right in rights else 0, validate=False)


def _workspace():
    links, blocks = [], []
    blocks.append({"id": "hdr", "type": "header", "data": {"text": '<span class="h4"><b>Recruitment</b></span>', "col": 12}})
    for i, (dt, label, _) in enumerate(SHORTCUTS):
        if frappe.db.exists("DocType", dt):
            blocks.append({"id": f"sc{i}", "type": "shortcut", "data": {"shortcut_name": label, "col": 3}})
    blocks.append({"id": "sp", "type": "spacer", "data": {"col": 12}})
    for i, (card, doctypes) in enumerate(SECTIONS):
        present = [d for d in doctypes if frappe.db.exists("DocType", d)]
        if not present:
            continue
        links.append({"type": "Card Break", "label": card, "link_count": len(present)})
        links += [{"type": "Link", "label": d, "link_type": "DocType", "link_to": d, "onboard": 0} for d in present]
        blocks.append({"id": f"card{i}", "type": "card", "data": {"card_name": card, "col": 4}})
    shortcuts = [{"type": "DocType", "link_to": dt, "label": label, "doc_view": "List", "color": "Grey",
                  "stats_filter": json.dumps([[dt, k, "=", v, False] for k, v in flt.items()])}
                 for dt, label, flt in SHORTCUTS if frappe.db.exists("DocType", dt)]
    values = {"label": WORKSPACE, "title": WORKSPACE, "module": "MoET Careers", "public": 1, "icon": "users",
              "content": json.dumps(blocks), "is_hidden": 0}
    if frappe.db.exists("Workspace", WORKSPACE):
        ws = frappe.get_doc("Workspace", WORKSPACE)
        ws.update(values)
    else:
        ws = frappe.get_doc({"doctype": "Workspace", **values})
    ws.set("links", links)
    ws.set("shortcuts", shortcuts)
    ws.set("roles", [{"role": r} for r in (RECRUITER, ADMIN, "System Manager", "HR Manager")])
    ws.flags.ignore_permissions = True
    ws.flags.ignore_links = True
    ws.save() if not ws.is_new() else ws.insert()
