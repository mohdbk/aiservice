"""Recruitment tools for the assistant. Every call runs as frappe.session.user, so Frappe's
normal role and record permissions apply. Write tools are split into preview (validates
and describes) and execute (runs only after the same user approves)."""

import frappe
from frappe import _
from frappe.utils import get_datetime, getdate

from ai_recruitment.setup import setting
from ai_recruitment.utils import free_slots, ics_event, overlaps, parse_criteria, strip_html

JA = "Job Applicant"
JA_FIELDS = ["name", "applicant_name", "job_title", "status", "ai_score", "ai_status", "ai_must_have_gaps"]
ALLOWED_STATUSES = ("Open", "Hold", "Replied")  # Reject / Accept stay manual


# ======================= read tools =======================
def list_job_openings(status: str = "Open"):
    return frappe.get_list("Job Opening", fields=["name", "job_title", "designation", "status"],
                           filters={"status": status}, order_by="creation desc", limit_page_length=50)


def get_job_opening(job_opening: str):
    doc = frappe.get_doc("Job Opening", job_opening)
    doc.check_permission("read")
    return {"name": doc.name, "job_title": doc.job_title, "designation": doc.designation, "status": doc.status,
            "description": strip_html(doc.description or "")[:4000],
            "ai_scoring_criteria": doc.get("ai_scoring_criteria") or ""}


def pipeline_status(job_opening: str):
    rows = frappe.get_list(JA, fields=JA_FIELDS, filters={"job_title": job_opening}, limit_page_length=1000)
    by_status, by_ai, scores = {}, {}, []
    for r in rows:
        by_status[r.status or "Open"] = by_status.get(r.status or "Open", 0) + 1
        by_ai[r.ai_status or "Pending"] = by_ai.get(r.ai_status or "Pending", 0) + 1
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


def search_applicants(query: str, job_opening: str | None = None, limit: int = 10):
    """Match name, email, CV/cover-letter text or AI summary/reasoning (skills, tools, employers)."""
    q = f"%{(query or '').strip()}%"
    if q == "%%":
        frappe.throw(_("Give a search term."))
    filters = [["job_title", "=", job_opening]] if job_opening else []
    or_filters = [["applicant_name", "like", q], ["email_id", "like", q], ["cover_letter", "like", q],
                  ["ai_summary", "like", q], ["ai_reasoning", "like", q]]
    return frappe.get_list(JA, fields=JA_FIELDS, filters=filters, or_filters=or_filters,
                           order_by="ai_score desc", limit_page_length=max(1, min(int(limit or 10), 25)))


def get_applicant_summary(applicant: str):
    doc = frappe.get_doc(JA, applicant)
    doc.check_permission("read")
    return {k: doc.get(k) for k in (
        "name", "applicant_name", "job_title", "status", "ai_score", "ai_status",
        "ai_summary", "ai_reasoning", "ai_must_have_gaps", "ai_model", "ai_scored_on")}


def compare_applicants(applicants: list):
    """Side-by-side data for 2-5 applicants."""
    if not isinstance(applicants, list) or not 2 <= len(applicants) <= 5:
        frappe.throw(_("Compare between 2 and 5 applicants."))
    return [get_applicant_summary(a) for a in applicants]


def get_interview_feedback(applicant: str):
    frappe.get_doc(JA, applicant).check_permission("read")
    interviews = frappe.get_list("Interview", filters={"job_applicant": applicant},
                                 fields=["name", "interview_round", "scheduled_on", "from_time", "to_time", "status"],
                                 order_by="scheduled_on asc", limit_page_length=20)
    feedback = frappe.get_list("Interview Feedback", filters={"job_applicant": applicant},
                               fields=["name", "interview", "interviewer", "result", "average_rating", "feedback"],
                               limit_page_length=50)
    for f in feedback:
        f["feedback"] = strip_html(f.get("feedback") or "")[:2000]
    return {"interviews": interviews, "feedback": feedback}


def rescore_applicant(applicant: str):
    frappe.get_doc(JA, applicant).check_permission("write")
    from ai_recruitment.scoring import enqueue

    frappe.db.set_value(JA, applicant, "ai_status", "Pending", update_modified=False)
    enqueue(applicant)
    return "Re-scoring queued; results appear on the applicant in under a minute."


def list_interview_rounds():
    return frappe.get_list("Interview Round", pluck="name", limit_page_length=50)


def list_interviewers():
    users = frappe.get_all("Has Role", filters={"role": "Interviewer", "parenttype": "User"},
                           pluck="parent", distinct=True)
    if not users:
        return []
    return frappe.get_all("User", filters={"name": ["in", users], "enabled": 1},
                          fields=["name", "full_name"], limit_page_length=100)


def _busy(interviewers: list, day: str) -> list:
    """(start, end) intervals on `day` from interviews and calendar events of these users."""
    busy = []
    parents = frappe.get_all("Interview Detail", filters={"interviewer": ["in", interviewers],
                                                          "parenttype": "Interview"}, pluck="parent")
    if parents:
        for iv in frappe.get_all("Interview", filters={"name": ["in", parents], "scheduled_on": day,
                                                       "status": ["!=", "Cancelled"]},
                                 fields=["from_time", "to_time"]):
            busy.append((get_datetime(f"{day} {iv.from_time}"), get_datetime(f"{day} {iv.to_time}")))
    for ev in frappe.get_all("Event", filters={"owner": ["in", interviewers],
                                               "starts_on": ["between", [f"{day} 00:00:00", f"{day} 23:59:59"]]},
                             fields=["starts_on", "ends_on", "all_day"]):
        if ev.all_day:
            busy.append((get_datetime(f"{day} 00:00:00"), get_datetime(f"{day} 23:59:59")))
        else:
            busy.append((get_datetime(ev.starts_on), get_datetime(ev.ends_on or ev.starts_on)))
    return busy


def find_interview_slots(interviewers: list, date: str, duration_minutes: int = 60):
    if not interviewers:
        frappe.throw(_("Name at least one interviewer."))
    day = str(getdate(date))
    start = str(setting("work_day_start", "09:00:00"))[:5]
    end = str(setting("work_day_end", "17:00:00"))[:5]
    return {"date": day, "working_hours": f"{start}-{end}",
            "free_slots": free_slots(day, _busy(interviewers, day), start, end, int(duration_minutes or 60))}


READ_TOOLS = {
    "list_job_openings": list_job_openings,
    "get_job_opening": get_job_opening,
    "pipeline_status": pipeline_status,
    "shortlist_applicants": shortlist_applicants,
    "search_applicants": search_applicants,
    "get_applicant_summary": get_applicant_summary,
    "compare_applicants": compare_applicants,
    "get_interview_feedback": get_interview_feedback,
    "rescore_applicant": rescore_applicant,
    "list_interview_rounds": list_interview_rounds,
    "list_interviewers": list_interviewers,
    "find_interview_slots": find_interview_slots,
}


# ======================= write tools =======================
def _applicant(applicant, ptype):
    doc = frappe.get_doc(JA, applicant)
    doc.check_permission(ptype)
    return doc


# ---- status ----
def preview_set_applicant_status(applicant: str, status: str) -> str:
    if status not in ALLOWED_STATUSES:
        frappe.throw(_("The assistant can only set Open, Hold or Replied."))
    doc = _applicant(applicant, "write")
    return f"Change {doc.applicant_name} ({doc.name}) from '{doc.status}' to '{status}'"


def execute_set_applicant_status(applicant: str, status: str) -> str:
    if status not in ALLOWED_STATUSES:
        frappe.throw(_("The assistant can only set Open, Hold or Replied."))
    doc = _applicant(applicant, "write")
    old, doc.status = doc.status, status
    doc.save()
    return f"{doc.applicant_name}: status changed from '{old}' to '{status}'."


# ---- interview ----
def _t(v: str) -> str:
    v = (v or "").strip()
    return v if len(v) > 5 else f"{v}:00"


def _interview_window(date, from_time, to_time):
    day = str(getdate(date))
    s, e = get_datetime(f"{day} {_t(from_time)}"), get_datetime(f"{day} {_t(to_time)}")
    if e <= s:
        frappe.throw(_("The end time must be after the start time."))
    return day, s, e


def _conflicts(interviewers, day, s, e):
    return [f"{b0:%H:%M}-{b1:%H:%M}" for b0, b1 in _busy(interviewers, day) if overlaps(s, e, b0, b1)]


def preview_schedule_interview(applicant, interview_round, date, from_time, to_time, interviewers=None) -> str:
    doc = _applicant(applicant, "read")
    if not frappe.has_permission("Interview", "create"):
        frappe.throw(_("You do not have permission to create interviews."))
    if not frappe.db.exists("Interview Round", interview_round):
        frappe.throw(_("Interview round {0} does not exist.").format(interview_round))
    interviewers = interviewers or []
    for u in interviewers:
        if not frappe.db.exists("User", {"name": u, "enabled": 1}):
            frappe.throw(_("Interviewer {0} is not an active user.").format(u))
    day, s, e = _interview_window(date, from_time, to_time)
    if interviewers:
        clash = _conflicts(interviewers, day, s, e)
        if clash:
            frappe.throw(_("Interviewers are busy at {0} on {1}. Use find_interview_slots to pick a free time.")
                         .format(", ".join(clash), day))
    who = f" with {', '.join(interviewers)}" if interviewers else ""
    invite = " and email calendar invites" if setting("send_interview_emails", 0) else ""
    return f"Schedule '{interview_round}' for {doc.applicant_name} ({doc.name}) on {day} {from_time}-{to_time}{who}{invite}"


def execute_schedule_interview(applicant, interview_round, date, from_time, to_time, interviewers=None) -> str:
    interviewers = interviewers or []
    app = _applicant(applicant, "read")
    day, s, e = _interview_window(date, from_time, to_time)
    if interviewers and _conflicts(interviewers, day, s, e):
        frappe.throw(_("The time is no longer free. Ask the assistant for new slots."))
    iv = frappe.get_doc({
        "doctype": "Interview",
        "interview_round": interview_round,
        "job_applicant": applicant,
        "scheduled_on": day,
        "from_time": _t(from_time),
        "to_time": _t(to_time),
        "status": "Pending",
        "interview_details": [{"interviewer": u} for u in interviewers],
    }).insert()

    sent = ""
    if setting("send_interview_emails", 0):
        recipients = list(interviewers) + ([app.email_id] if app.email_id else [])
        if recipients:
            try:
                ics = ics_event(f"{iv.name}@{frappe.local.site}", s, e,
                                f"Interview: {app.applicant_name} ({interview_round})",
                                f"Job opening: {app.job_title}\nInterview {iv.name}",
                                organizer=frappe.session.user if "@" in frappe.session.user else "")
                frappe.sendmail(
                    recipients=recipients,
                    subject=f"Interview invitation: {interview_round} on {day} {from_time}",
                    message=(f"<p>An interview has been scheduled.</p><p><b>{interview_round}</b><br>"
                             f"{day}, {from_time} to {to_time} (UAE time)</p>"),
                    attachments=[{"fname": "interview.ics", "fcontent": ics}],
                    reference_doctype="Interview", reference_name=iv.name,
                )
                sent = f" Calendar invites queued to {len(recipients)} recipient(s)."
            except Exception:
                frappe.log_error(title=f"Interview invite failed: {iv.name}")
                sent = " Invites could not be sent (check the outgoing email account)."
    return f"Interview {iv.name} created for {day} {from_time}-{to_time}.{sent}"


# ---- job opening ----
def _default_company():
    return frappe.defaults.get_user_default("Company") or frappe.db.get_single_value("Global Defaults", "default_company") \
        or (frappe.get_all("Company", pluck="name", limit_page_length=1) or [None])[0]


def preview_create_job_opening(job_title, designation, description, scoring_criteria="", publish=False) -> str:
    if not frappe.has_permission("Job Opening", "create"):
        frappe.throw(_("You do not have permission to create job openings."))
    crit = parse_criteria(scoring_criteria)
    new_des = "" if frappe.db.exists("Designation", designation) else f" (new designation '{designation}')"
    return (f"Create job opening '{job_title}'{new_des}, {len(crit)} scoring criteria, "
            f"{'published on the careers page' if publish else 'not published yet'}")


def execute_create_job_opening(job_title, designation, description, scoring_criteria="", publish=False) -> str:
    if not frappe.db.exists("Designation", designation):
        frappe.get_doc({"doctype": "Designation", "designation_name": designation}).insert()
    doc = frappe.get_doc({
        "doctype": "Job Opening",
        "job_title": job_title,
        "designation": designation,
        "company": _default_company(),
        "status": "Open",
        "publish": 1 if publish else 0,
        "description": frappe.utils.md_to_html(description) if description else "",
        "ai_scoring_criteria": scoring_criteria or "",
    }).insert()
    return f"Job opening {doc.name} created{' and published' if publish else ' (not published)'}."


# ---- rubric ----
def preview_update_scoring_criteria(job_opening, scoring_criteria) -> str:
    doc = frappe.get_doc("Job Opening", job_opening)
    doc.check_permission("write")
    crit = parse_criteria(scoring_criteria)
    if not crit:
        frappe.throw(_("No valid criteria. Use lines like: 30 | 3+ years Python | must"))
    lines = "; ".join(f"{c['weight']:g} {c['criterion']}{' (must)' if c['must'] else ''}" for c in crit)
    return f"Replace the scoring criteria of {doc.job_title} ({doc.name}) with: {lines}"


def execute_update_scoring_criteria(job_opening, scoring_criteria) -> str:
    doc = frappe.get_doc("Job Opening", job_opening)
    doc.check_permission("write")
    doc.ai_scoring_criteria = scoring_criteria
    doc.save()
    return f"Scoring criteria updated for {doc.name}. Re-score applicants to apply them."


# ---- email ----
def preview_send_candidate_email(applicant, subject, body) -> str:
    doc = _applicant(applicant, "read")
    if not doc.email_id:
        frappe.throw(_("This applicant has no email address."))
    if not frappe.has_permission("Communication", "create"):
        frappe.throw(_("You do not have permission to send emails."))
    return f"Email {doc.applicant_name} <{doc.email_id}>: '{subject}'"


def execute_send_candidate_email(applicant, subject, body) -> str:
    from frappe.core.doctype.communication.email import make

    doc = _applicant(applicant, "read")
    make(doctype=JA, name=applicant, subject=subject, content=frappe.utils.md_to_html(body),
         recipients=doc.email_id, send_email=True, communication_medium="Email")
    return f"Email to {doc.email_id} queued and recorded on the applicant."


WRITE_TOOLS = {
    "set_applicant_status": (preview_set_applicant_status, execute_set_applicant_status),
    "schedule_interview": (preview_schedule_interview, execute_schedule_interview),
    "create_job_opening": (preview_create_job_opening, execute_create_job_opening),
    "update_scoring_criteria": (preview_update_scoring_criteria, execute_update_scoring_criteria),
    "send_candidate_email": (preview_send_candidate_email, execute_send_candidate_email),
}

WRITE_TARGET = {
    "set_applicant_status": ("Job Applicant", "applicant"),
    "schedule_interview": ("Job Applicant", "applicant"),
    "send_candidate_email": ("Job Applicant", "applicant"),
    "update_scoring_criteria": ("Job Opening", "job_opening"),
}
