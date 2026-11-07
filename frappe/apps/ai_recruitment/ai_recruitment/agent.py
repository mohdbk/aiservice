"""Tool-calling loop for the recruitment assistant.

- Runs as the session user; tools obey Frappe permissions.
- Writes become server-side proposals bound to the user; they run only on Approve.
- History comes from the user's own AI Chat Log (server-side memory per session).
- Status and text stream to the user's browser over Frappe realtime while it works.
- Every turn and approval is written to AI Chat Log.
"""
import json
import time

import frappe

from ai_recruitment import governance as G
from ai_recruitment import tools as T
from ai_recruitment.llm import complete, config
from ai_recruitment.utils import WRITING_RULE, no_em_dash

ACTION_TTL = 30 * 60
EVENT = "ai_recruitment_stream"

SYSTEM = f"""You are the Ministry of Economy & Tourism's recruitment assistant inside Frappe HR.
- Writing style: {WRITING_RULE}
- Reply in the user's language (Arabic or English). Be concise. Markdown is rendered: use short lists, **bold** and small tables.
- Use the tools; never guess IDs, scores, people or statuses. Applicants and job openings are referenced by ID.
- To find people by skill, employer or name use search_applicants. To compare, use compare_applicants and answer with a table.
- AI scores are recommendations only. Humans make hiring decisions; you cannot reject, accept or delete anything.
- These only PROPOSE an action that waits for the user's Approve button: set_applicant_status, schedule_interview,
  create_job_opening, update_scoring_criteria, send_candidate_email. Never claim a proposed action is done.
- Before schedule_interview with interviewers, call find_interview_slots and propose a free slot.
- When drafting a job description, write it in clear Markdown. When drafting scoring criteria, use lines of
  "weight | criterion | must" with weights summing to 100 and at most 3 must-haves; keep criteria job-related and
  never use age, gender, nationality, religion, marital status or family situation.
- Candidate emails must be polite and neutral. Never state or imply an AI made a hiring decision.
- The user's current Frappe page is given as context; "this applicant" or "this job" refers to it."""


def _fn(name, desc, props, required):
    return {"type": "function", "function": {
        "name": name, "description": desc,
        "parameters": {"type": "object", "properties": props, "required": required, "additionalProperties": False}}}


S = {"type": "string"}
LIST_S = {"type": "array", "items": {"type": "string"}}
TOOL_SPECS = [
    _fn("list_job_openings", "List job openings.", {"status": {"type": "string", "enum": ["Open", "Closed"]}}, []),
    _fn("get_job_opening", "Description and scoring criteria of a job opening.", {"job_opening": S}, ["job_opening"]),
    _fn("pipeline_status", "Applicant counts and AI scoring progress for a job opening.", {"job_opening": S}, ["job_opening"]),
    _fn("shortlist_applicants", "Top applicants for a job opening ranked by AI score.",
        {"job_opening": S, "top_n": {"type": "integer"}, "min_score": {"type": "number"}}, ["job_opening"]),
    _fn("search_applicants", "Search applicants by name, email, skill, tool or employer in CV text and AI summaries.",
        {"query": S, "job_opening": S, "limit": {"type": "integer"}}, ["query"]),
    _fn("get_applicant_summary", "AI summary, reasoning and status of one applicant.", {"applicant": S}, ["applicant"]),
    _fn("compare_applicants", "Data for 2-5 applicants side by side.", {"applicants": LIST_S}, ["applicants"]),
    _fn("get_interview_feedback", "Interviews and interviewer feedback for an applicant.", {"applicant": S}, ["applicant"]),
    _fn("rescore_applicant", "Queue AI re-scoring for one applicant.", {"applicant": S}, ["applicant"]),
    _fn("list_interview_rounds", "Interview rounds available for scheduling.", {}, []),
    _fn("list_interviewers", "Users with the Interviewer role.", {}, []),
    _fn("find_interview_slots", "Free slots for interviewers on a date (checks interviews and calendar events).",
        {"interviewers": LIST_S, "date": S, "duration_minutes": {"type": "integer"}}, ["interviewers", "date"]),
    _fn("set_applicant_status", "PROPOSE moving an applicant to Open, Hold or Replied.",
        {"applicant": S, "status": {"type": "string", "enum": list(T.ALLOWED_STATUSES)}}, ["applicant", "status"]),
    _fn("schedule_interview", "PROPOSE an interview. date YYYY-MM-DD, times HH:MM UAE time, interviewers are user IDs.",
        {"applicant": S, "interview_round": S, "date": S, "from_time": S, "to_time": S, "interviewers": LIST_S},
        ["applicant", "interview_round", "date", "from_time", "to_time"]),
    _fn("create_job_opening", "PROPOSE a new job opening with a Markdown description and scoring criteria.",
        {"job_title": S, "designation": S, "description": S, "scoring_criteria": S, "publish": {"type": "boolean"}},
        ["job_title", "designation", "description"]),
    _fn("update_scoring_criteria", "PROPOSE replacing a job opening's scoring criteria.",
        {"job_opening": S, "scoring_criteria": S}, ["job_opening", "scoring_criteria"]),
    _fn("send_candidate_email", "PROPOSE an email to an applicant (Markdown body).",
        {"applicant": S, "subject": S, "body": S}, ["applicant", "subject", "body"]),
]

STATUS_TEXT = {
    "search_applicants": "Searching applicants", "shortlist_applicants": "Ranking applicants",
    "compare_applicants": "Comparing applicants", "pipeline_status": "Checking the pipeline",
    "get_applicant_summary": "Reading the applicant", "get_interview_feedback": "Reading interview feedback",
    "find_interview_slots": "Checking calendars", "get_job_opening": "Reading the job opening",
}


class _Streamer:
    """Batches text deltas into realtime events to the current user."""

    def __init__(self, request_id):
        self.request_id, self.buf, self.last = request_id, "", 0.0

    def send(self, kind, text):
        if not self.request_id:
            return
        frappe.publish_realtime(EVENT, {"request_id": self.request_id, "type": kind, "text": text},
                                user=frappe.session.user, after_commit=False)

    def status(self, text):
        self.send("status", frappe._(text) + "…")

    def delta(self, text):
        self.buf += no_em_dash(text)
        if len(self.buf) >= 60 or time.time() - self.last > 0.3:
            self.flush()

    def flush(self):
        if self.buf:
            self.send("delta", self.buf)
            self.buf, self.last = "", time.time()


def _propose(name, args, session_id):
    preview_fn, _ = T.WRITE_TOOLS[name]
    label = no_em_dash(preview_fn(**args))  # validates permissions before offering the button
    action_id = frappe.generate_hash(length=20)
    frappe.cache.set_value(f"ai_recruitment:action:{action_id}",
                           {"tool": name, "args": args, "user": frappe.session.user, "label": label,
                            "session_id": session_id}, expires_in_sec=ACTION_TTL)
    return {"id": action_id, "label": label}


def run(message: str, session_id: str, context: dict, request_id: str | None = None) -> dict:
    started = time.time()
    stream = _Streamer(request_id)
    trace, actions, usage_total = [], [], {"prompt_tokens": 0, "completion_tokens": 0}
    page = (context or {}).get("route") or ""
    base_log = {"log_type": "Chat", "session_id": session_id, "question": message, "page_context": page[:140],
                "model": config().model}

    try:
        G.check_and_count_message()
    except G.RateLimited as e:
        G.log_turn(**base_log, status="Rate Limited", reply=str(e))
        return {"session_id": session_id, "reply": str(e), "actions": []}

    msgs = [{"role": "system", "content": SYSTEM},
            {"role": "system", "content": (
                f"Current user: {frappe.session.user} ({frappe.utils.get_fullname(frappe.session.user)}). "
                f"Today: {frappe.utils.today()}. Current page: {json.dumps(context or {})[:500]}")}]
    msgs += G.history(session_id)
    msgs.append({"role": "user", "content": (message or "")[:4000]})

    try:
        reply = None
        for _step in range(8):
            stream.status("Thinking")
            msg, usage = complete(msgs, tools=TOOL_SPECS, temperature=0.2, timeout=120, on_delta=stream.delta)
            stream.flush()
            for k in usage_total:
                usage_total[k] += int(usage.get(k) or 0)
            calls = msg.get("tool_calls") or []
            if not calls:
                reply = no_em_dash((msg.get("content") or "").strip())
                break
            msgs.append({"role": "assistant", "content": msg.get("content") or "", "tool_calls": calls})
            stream.send("reset", "")  # discard any pre-tool chatter from the bubble
            for call in calls:
                name = call["function"]["name"]
                try:
                    args = json.loads(call["function"].get("arguments") or "{}")
                except json.JSONDecodeError:
                    args = {}
                stream.status(STATUS_TEXT.get(name, "Working"))
                try:
                    if name in T.READ_TOOLS:
                        result = T.READ_TOOLS[name](**args)
                    elif name in T.WRITE_TOOLS:
                        a = _propose(name, args, session_id)
                        actions.append(a)
                        result = "Proposed and shown to the user with an Approve button. NOT applied yet: " + a["label"]
                    else:
                        result = f"Unknown tool {name}"
                except frappe.PermissionError:
                    result = "Permission denied for the current user."
                    frappe.clear_messages()
                except Exception as e:
                    result = f"Error: {str(e)[:300]}"
                    frappe.clear_messages()
                trace.append({"tool": name, "args": args, "result": str(result)[:600]})
                msgs.append({"role": "tool", "tool_call_id": call["id"],
                             "content": json.dumps(result, default=str, ensure_ascii=False)[:8000]})
        if reply is None:
            reply = "I could not finish that in a few steps. Try a narrower question."
        G.count_tokens(usage_total["prompt_tokens"] + usage_total["completion_tokens"])
        G.log_turn(**base_log, status="Success", reply=reply, tool_trace=trace, actions_proposed=actions,
                   latency_ms=int((time.time() - started) * 1000), **usage_total)
        return {"session_id": session_id, "reply": reply, "actions": actions}
    except Exception as e:
        # Keep the audit entry even though the request fails (the error would roll it back).
        frappe.db.rollback()
        G.log_turn(**base_log, status="Error", error=str(e)[:500], tool_trace=trace,
                   latency_ms=int((time.time() - started) * 1000), **usage_total)
        frappe.db.commit()
        raise


def approve(action_id: str) -> str:
    key = f"ai_recruitment:action:{action_id}"
    a = frappe.cache.get_value(key)
    if not a:
        frappe.throw("This proposal has expired or was already used. Ask the assistant again.")
    if a.get("user") != frappe.session.user:
        frappe.throw("This proposal belongs to another user.", frappe.PermissionError)
    frappe.cache.delete_value(key)  # single use

    _, execute_fn = T.WRITE_TOOLS[a["tool"]]
    try:
        result = no_em_dash(execute_fn(**a["args"]))
    except Exception as e:
        frappe.db.rollback()  # undo any partial change first, then keep the audit entry
        G.log_turn(log_type="Approval", session_id=a.get("session_id"), status="Error", question=a["label"],
                   error=str(e)[:500], tool_trace=[{"tool": a["tool"], "args": a["args"]}])
        frappe.db.commit()
        raise
    G.log_turn(log_type="Approval", session_id=a.get("session_id"), status="Success", question=a["label"],
               reply=result, tool_trace=[{"tool": a["tool"], "args": a["args"]}])

    doctype, field = T.WRITE_TARGET.get(a["tool"], (None, None))
    target = a["args"].get(field) if field else None
    if target and frappe.db.exists(doctype, target):
        frappe.get_doc(doctype, target).add_comment(
            "Info", f"Approved via AI Recruitment Assistant by {frappe.session.user}: {a['label']}.")
    return result
