"""Recruitment tools for the assistant. Every call runs as frappe.session.user,
so Frappe's normal role and record permissions apply."""
import frappe
from frappe import _

JA = "Job Applicant"
JA_FIELDS = ["name", "applicant_name", "job_title", "status", "ai_score", "ai_status", "ai_must_have_gaps"]
ALLOWED_STATUSES = ("Open", "Hold", "Replied")  # Reject / Accept stay manual


# ---------------- read tools ----------------
def list_job_openings(status: str = "Open"):
    return frappe.get_list("Job Opening", fields=["name", "job_title", "designation", "status"],
                           filters={"status": status}, order_by="creation desc", limit_page_length=50)


def pipeline_status(job_opening: str):
    rows = frappe.get_list(JA, fields=JA_FIELDS, filters={"job_title": job_opening}, limit_page_length=1000)
    by_status, by_ai, scores = {}, {}, []
    for r in rows:
        st = r.status or "Open"
        ai = r.ai_status or "Pending"
        by_status[st] = by_status.get(st, 0) + 1
        by_ai[ai] = by_ai.get(ai, 0) + 1
        if r.ai_status == "Scored":
            scores.append(r.ai_score or 0)
    return {"job_opening": job_opening, "total_applicants": len(rows), "by_status": by_status,
            "by_ai_status": by_ai, "average_ai_score": round(sum(scores) / len(scores), 1) if scores else None}


def shortlist_applicants(job_opening: str, top_n: int = 5, min_score: float = 0):
    return frappe.get_list(
        JA, fields=JA_FIELDS,
        filters=[["job_title", "=", job_opening], ["ai_status", "=", "Scored"],
                 ["ai_score", ">=", float(min_score or 0)], ["status", "!=", "Rejected"]],
        order_by="ai_score desc", limit_page_length=max(1, min(int(top_n or 5), 50)),
    )


def get_applicant_summary(applicant: str):
    doc = frappe.get_doc(JA, applicant)
    doc.check_permission("read")
    return {k: doc.get(k) for k in (
        "name", "applicant_name", "job_title", "status", "ai_score", "ai_status",
        "ai_summary", "ai_reasoning", "ai_must_have_gaps", "ai_model", "ai_scored_on")}


def rescore_applicant(applicant: str):
    frappe.get_doc(JA, applicant).check_permission("write")
    from ai_recruitment.scoring import enqueue

    frappe.db.set_value(JA, applicant, "ai_status", "Pending", update_modified=False)
    enqueue(applicant)
    return "Re-scoring queued; results appear on the applicant in under a minute."


def list_interview_rounds():
    return frappe.get_list("Interview Round", pluck="name", limit_page_length=50)


READ_TOOLS = {
    "list_job_openings": list_job_openings,
    "pipeline_status": pipeline_status,
    "shortlist_applicants": shortlist_applicants,
    "get_applicant_summary": get_applicant_summary,
    "rescore_applicant": rescore_applicant,
    "list_interview_rounds": list_interview_rounds,
}


# ---------------- write tools: preview, then execute after human approval ----------------
def _applicant(applicant, ptype):
    doc = frappe.get_doc(JA, applicant)
    doc.check_permission(ptype)
    return doc


def preview_set_applicant_status(applicant: str, status: str) -> str:
    if status not in ALLOWED_STATUSES:
        frappe.throw(_("The assistant can only set Open, Hold or Replied."))
    doc = _applicant(applicant, "write")
    return f"Change {doc.applicant_name} ({doc.name}) from '{doc.status}' to '{status}'"


def execute_set_applicant_status(applicant: str, status: str) -> str:
    if status not in ALLOWED_STATUSES:
        frappe.throw(_("The assistant can only set Open, Hold or Replied."))
    doc = _applicant(applicant, "write")
    old = doc.status
    doc.status = status
    doc.save()
    return f"{doc.applicant_name}: status changed from '{old}' to '{status}'."


def _t(v: str) -> str:
    return v if len(v or "") > 5 else f"{v}:00"


def preview_schedule_interview(applicant, interview_round, date, from_time, to_time) -> str:
    doc = _applicant(applicant, "read")
    if not frappe.has_permission("Interview", "create"):
        frappe.throw(_("You do not have permission to create interviews."))
    if not frappe.db.exists("Interview Round", interview_round):
        frappe.throw(_("Interview round {0} does not exist.").format(interview_round))
    return f"Schedule '{interview_round}' for {doc.applicant_name} ({doc.name}) on {date} {from_time}-{to_time}"


def execute_schedule_interview(applicant, interview_round, date, from_time, to_time) -> str:
    doc = frappe.get_doc({
        "doctype": "Interview",
        "interview_round": interview_round,
        "job_applicant": applicant,
        "scheduled_on": date,
        "from_time": _t(from_time),
        "to_time": _t(to_time),
        "status": "Pending",
    }).insert()
    return f"Draft interview {doc.name} created for {date} {from_time}-{to_time}."


WRITE_TOOLS = {
    "set_applicant_status": (preview_set_applicant_status, execute_set_applicant_status),
    "schedule_interview": (preview_schedule_interview, execute_schedule_interview),
}

WRITE_TARGET = {"set_applicant_status": "applicant", "schedule_interview": "applicant"}
