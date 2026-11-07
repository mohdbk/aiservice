"""Tool-calling loop for the recruitment assistant.

Read tools run directly as the session user. Write tools only produce a pending
action stored server-side, bound to this user; it runs only when the same user
clicks Approve (api.approve).
"""
import json

import frappe

from ai_recruitment import tools as T
from ai_recruitment.llm import WRITING_RULE, chat_completion, no_em_dash

ACTION_TTL = 30 * 60

SYSTEM = f"""You are the ministry's recruitment assistant inside Frappe HR.
- Writing style: {WRITING_RULE}
- Reply in the user's language (Arabic or English). Be concise. Markdown is rendered: use short lists, **bold** and small tables where helpful.
- Use the tools to answer; never guess IDs, scores or statuses. Job openings and applicants are referenced by their ID.
- AI scores are recommendations only. Humans make hiring decisions; you cannot reject, accept or delete.
- Changing a status or scheduling an interview only PROPOSES the action. Say it is waiting for the user's Approve button; never claim it is done.
- The user's current Frappe page is given as context; "this applicant" or "this job" refers to it."""


def _fn(name, desc, props, required):
    return {"type": "function", "function": {
        "name": name, "description": desc,
        "parameters": {"type": "object", "properties": props, "required": required, "additionalProperties": False}}}


S = {"type": "string"}
TOOL_SPECS = [
    _fn("list_job_openings", "List job openings.", {"status": {"type": "string", "enum": ["Open", "Closed"]}}, []),
    _fn("pipeline_status", "Applicant counts and AI scoring progress for a job opening.", {"job_opening": S}, ["job_opening"]),
    _fn("shortlist_applicants", "Top applicants for a job opening ranked by AI score.",
        {"job_opening": S, "top_n": {"type": "integer"}, "min_score": {"type": "number"}}, ["job_opening"]),
    _fn("get_applicant_summary", "AI summary, reasoning and status of one applicant.", {"applicant": S}, ["applicant"]),
    _fn("rescore_applicant", "Queue AI re-scoring for one applicant.", {"applicant": S}, ["applicant"]),
    _fn("list_interview_rounds", "Interview rounds available for scheduling.", {}, []),
    _fn("set_applicant_status", "PROPOSE moving an applicant to Open, Hold or Replied (needs the user's approval).",
        {"applicant": S, "status": {"type": "string", "enum": list(T.ALLOWED_STATUSES)}}, ["applicant", "status"]),
    _fn("schedule_interview", "PROPOSE a draft interview (needs the user's approval). date YYYY-MM-DD, times HH:MM UAE time.",
        {"applicant": S, "interview_round": S, "date": S, "from_time": S, "to_time": S},
        ["applicant", "interview_round", "date", "from_time", "to_time"]),
]


def _cache_key(action_id: str) -> str:
    return f"ai_recruitment:action:{action_id}"


def _propose(name: str, args: dict) -> dict:
    preview_fn, _ = T.WRITE_TOOLS[name]
    label = preview_fn(**args)  # validates permissions now, before offering the button
    action_id = frappe.generate_hash(length=20)
    frappe.cache.set_value(_cache_key(action_id),
                           {"tool": name, "args": args, "user": frappe.session.user, "label": label},
                           expires_in_sec=ACTION_TTL)
    return {"id": action_id, "label": label}


def run(history: list, context: dict) -> dict:
    msgs = [
        {"role": "system", "content": SYSTEM},
        {"role": "system", "content": (
            f"Current user: {frappe.session.user} ({frappe.utils.get_fullname(frappe.session.user)}). "
            f"Today: {frappe.utils.today()}. Current page: {json.dumps(context or {})[:500]}")},
    ]
    for m in (history or [])[-20:]:
        if isinstance(m, dict) and m.get("role") in ("user", "assistant") and isinstance(m.get("content"), str):
            msgs.append({"role": m["role"], "content": m["content"][:4000]})

    actions = []
    for _step in range(6):
        msg = chat_completion(msgs, tools=TOOL_SPECS, temperature=0.2, timeout=120)
        calls = msg.get("tool_calls") or []
        if not calls:
            return {"reply": no_em_dash((msg.get("content") or "").strip()), "actions": actions}
        msgs.append({"role": "assistant", "content": msg.get("content") or "", "tool_calls": calls})
        for call in calls:
            name = call["function"]["name"]
            try:
                args = json.loads(call["function"].get("arguments") or "{}")
            except json.JSONDecodeError:
                args = {}
            try:
                if name in T.READ_TOOLS:
                    result = T.READ_TOOLS[name](**args)
                elif name in T.WRITE_TOOLS:
                    a = _propose(name, args)
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
            msgs.append({"role": "tool", "tool_call_id": call["id"],
                         "content": json.dumps(result, default=str)[:8000]})
    return {"reply": "I couldn't finish that in a few steps. Try a narrower question.", "actions": actions}


def approve(action_id: str) -> str:
    key = _cache_key(action_id)
    a = frappe.cache.get_value(key)
    if not a:
        frappe.throw("This proposal has expired or was already used. Ask the assistant again.")
    if a.get("user") != frappe.session.user:
        frappe.throw("This proposal belongs to another user.", frappe.PermissionError)
    frappe.cache.delete_value(key)  # single use

    _, execute_fn = T.WRITE_TOOLS[a["tool"]]
    result = execute_fn(**a["args"])

    target = a["args"].get(T.WRITE_TARGET.get(a["tool"], ""), "")
    if target and frappe.db.exists("Job Applicant", target):
        frappe.get_doc("Job Applicant", target).add_comment(
            "Info", f"Approved via AI Recruitment Assistant by {frappe.session.user}: {a['label']}.")
    return result
