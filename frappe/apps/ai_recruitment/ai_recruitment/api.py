"""Whitelisted endpoints called by the Desk chat panel with the user's own session."""
import frappe
from frappe import _

from ai_recruitment import agent
from ai_recruitment import governance as G
from ai_recruitment.setup import chat_allowed


def _guard():
    if frappe.session.user == "Guest" or not chat_allowed():
        frappe.throw(_("You are not allowed to use the Recruitment Assistant."), frappe.PermissionError)


@frappe.whitelist(methods=["POST"])
def chat(message: str, session_id: str | None = None, context=None, request_id: str | None = None):
    _guard()
    session_id = (session_id or "")[:40] or frappe.generate_hash(length=16)
    return agent.run(message, session_id, frappe.parse_json(context) or {}, (request_id or "")[:40] or None)


@frappe.whitelist(methods=["POST"])
def history(session_id: str | None = None):
    """The current user's own conversation (latest session if none given)."""
    _guard()
    session_id = session_id or G.latest_session()
    return {"session_id": session_id, "messages": G.history(session_id, turns=30) if session_id else []}


@frappe.whitelist(methods=["POST"])
def approve(action_id: str):
    _guard()
    return {"result": agent.approve(action_id)}


@frappe.whitelist(methods=["POST"])
def rescore(applicant: str):
    _guard()
    from ai_recruitment.tools import rescore_applicant

    return rescore_applicant(applicant)


@frappe.whitelist(methods=["POST"])
def rescore_unscored(job_opening: str | None = None):
    """Queue every Pending/Failed/unscored applicant (HR Manager / System Manager)."""
    frappe.only_for(["HR Manager", "System Manager"])
    from ai_recruitment.scoring import enqueue

    filters = [["ai_status", "in", ["", "Pending", "Failed"]]]
    if job_opening:
        filters.append(["job_title", "=", job_opening])
    names = frappe.get_list("Job Applicant", filters=filters, pluck="name", limit_page_length=1000)
    for n in names:
        enqueue(n)
    return {"queued": len(names)}


@frappe.whitelist()
def export_json_resume(applicant: str):
    """The applicant's parsed profile in JSON Resume v1 format (requires read access)."""
    import json

    from ai_recruitment.utils import to_json_resume

    doc = frappe.get_doc("Job Applicant", applicant)
    doc.check_permission("read")
    profile = json.loads(doc.get("ai_profile") or "{}")
    if not profile:
        frappe.throw(_("This applicant has no parsed profile yet. Re-score with AI first."))
    return to_json_resume(profile, name=doc.applicant_name, email=doc.email_id or "",
                          phone=doc.get("phone_number") or "",
                          meta={"source": "Frappe HR ai_recruitment", "applicant": doc.name,
                                "jobOpening": doc.job_title, "parsedOn": str(doc.get("ai_scored_on") or ""),
                                "cvLanguage": doc.get("ai_cv_language") or "",
                                "yearsOfExperience": profile.get("years_of_experience"),
                                "experienceBasis": profile.get("years_basis", "")})
