"""In-Frappe chat agent: LLM tool-calling over the recruitment tools.

Read tools run directly. Write tools never run from the model: they return a
signed pending action that the UI shows with an Approve button, and only a
human click executes it (/chat/approve).
"""
import base64
import hashlib
import hmac
import json
import logging

import httpx

from . import mcp_tools as t
from .config import settings

log = logging.getLogger("agent")

SYSTEM = """You are the ministry's recruitment assistant inside Frappe HR.
- Reply in the user's language (Arabic or English). Be concise.
- Use the tools to answer; never guess IDs, scores or statuses. Job openings and applicants are referenced by their ID.
- AI scores are recommendations only. Humans make hiring decisions; you cannot reject, accept or delete.
- Changing a status or scheduling an interview only PROPOSES the action. Say it is waiting for the user's Approve button; never claim it is done.
- The user's current Frappe page is given as context; "this applicant" or "this job" refers to it."""


def _fn(name, desc, props, required):
    return {"type": "function", "function": {
        "name": name, "description": desc,
        "parameters": {"type": "object", "properties": props, "required": required, "additionalProperties": False},
    }}


S = {"type": "string"}
TOOLS = [
    _fn("list_job_openings", "List job openings.", {"status": {"type": "string", "enum": ["Open", "Closed"]}}, []),
    _fn("pipeline_status", "Applicant counts and AI scoring progress for a job opening.", {"job_opening": S}, ["job_opening"]),
    _fn("shortlist_applicants", "Top applicants for a job opening ranked by AI score.",
        {"job_opening": S, "top_n": {"type": "integer"}, "min_score": {"type": "number"}}, ["job_opening"]),
    _fn("get_applicant_summary", "AI summary, reasoning and status of one applicant.", {"applicant": S}, ["applicant"]),
    _fn("rescore_applicant", "Re-run AI scoring for one applicant.", {"applicant": S}, ["applicant"]),
    _fn("list_interview_rounds", "Interview rounds available for scheduling.", {}, []),
    _fn("set_applicant_status", "PROPOSE moving an applicant to Open, Hold or Replied (needs human approval).",
        {"applicant": S, "status": {"type": "string", "enum": ["Open", "Hold", "Replied"]}}, ["applicant", "status"]),
    _fn("schedule_interview", "PROPOSE a draft interview (needs human approval). date YYYY-MM-DD, times HH:MM UAE.",
        {"applicant": S, "interview_round": S, "date": S, "from_time": S, "to_time": S},
        ["applicant", "interview_round", "date", "from_time", "to_time"]),
]

READ = {
    "list_job_openings": t.list_job_openings,
    "pipeline_status": t.pipeline_status,
    "shortlist_applicants": t.shortlist_applicants,
    "get_applicant_summary": t.get_applicant_summary,
    "rescore_applicant": t.rescore_applicant,
    "list_interview_rounds": t.list_interview_rounds,
}
WRITE = {"set_applicant_status": t.set_applicant_status, "schedule_interview": t.schedule_interview}


# ---- signed pending actions (stateless, tamper-proof) ----
def _key() -> bytes:
    return (settings.mcp_token or "dev").encode()


def sign_action(tool: str, args: dict, user: str) -> str:
    payload = base64.urlsafe_b64encode(json.dumps({"tool": tool, "args": args, "user": user}).encode()).decode()
    sig = hmac.new(_key(), payload.encode(), hashlib.sha256).hexdigest()
    return f"{payload}.{sig}"


def verify_action(token: str) -> dict:
    payload, _, sig = token.rpartition(".")
    good = hmac.new(_key(), payload.encode(), hashlib.sha256).hexdigest()
    if not payload or not hmac.compare_digest(sig, good):
        raise ValueError("Invalid action")
    return json.loads(base64.urlsafe_b64decode(payload))


async def _llm(messages: list[dict]) -> dict:
    headers = {"Content-Type": "application/json"}
    if settings.llm_api_key:
        headers["Authorization"] = f"Bearer {settings.llm_api_key}"
    body = {"model": settings.llm_model, "messages": messages, "tools": TOOLS,
            "tool_choice": "auto", "temperature": 0.2}
    if "openrouter.ai" in settings.llm_base_url:
        body["provider"] = {"require_parameters": True, "data_collection": "deny"}
        body["reasoning"] = {"enabled": False}
    async with httpx.AsyncClient(timeout=120) as c:
        r = await c.post(f"{settings.llm_base_url}/chat/completions", headers=headers, json=body)
        if r.status_code == 400 and "reasoning" in body:
            body.pop("reasoning")
            r = await c.post(f"{settings.llm_base_url}/chat/completions", headers=headers, json=body)
        r.raise_for_status()
        return r.json()["choices"][0]["message"]


async def chat(history: list[dict], context: dict, user: str) -> dict:
    """history: [{role: user|assistant, content}] from the UI."""
    msgs = [{"role": "system", "content": SYSTEM},
            {"role": "system", "content": f"Current user: {user}. Current page: {json.dumps(context)[:500]}"}]
    for m in history[-20:]:
        if m.get("role") in ("user", "assistant") and isinstance(m.get("content"), str):
            msgs.append({"role": m["role"], "content": m["content"][:4000]})

    actions: list[dict] = []
    for _ in range(6):
        msg = await _llm(msgs)
        calls = msg.get("tool_calls") or []
        if not calls:
            return {"reply": (msg.get("content") or "").strip(), "actions": actions}
        msgs.append({"role": "assistant", "content": msg.get("content") or "", "tool_calls": calls})
        for call in calls:
            name = call["function"]["name"]
            try:
                args = json.loads(call["function"].get("arguments") or "{}")
            except json.JSONDecodeError:
                args = {}
            try:
                if name in READ:
                    result = await READ[name](**args)
                elif name in WRITE:
                    preview = await WRITE[name](**args, confirm=False)
                    label = preview.replace("PREVIEW — not applied. ", "").split(" Ask the user")[0]
                    actions.append({"label": label, "token": sign_action(name, args, user)})
                    result = "Proposed and shown to the user with an Approve button. Not applied yet: " + label
                else:
                    result = f"Unknown tool {name}"
            except Exception as e:
                result = f"Error: {str(e)[:300]}"
            msgs.append({"role": "tool", "tool_call_id": call["id"],
                         "content": json.dumps(result, default=str)[:8000]})
    return {"reply": "I couldn't finish that in a few steps — try a narrower question.", "actions": actions}


async def approve(token: str, user: str) -> str:
    a = verify_action(token)
    if a.get("user") != user:
        raise ValueError("This action was proposed to a different user")
    fn = WRITE.get(a["tool"])
    if not fn:
        raise ValueError("Unknown action")
    log.info("Approved by %s: %s %s", user, a["tool"], a["args"])
    return await fn(**a["args"], confirm=True)
