"""Audit logging, retention and per-user usage limits for the assistant."""
import json
import time

import frappe
from frappe.utils import add_days, nowdate

from ai_recruitment.setup import setting


# ---------------- audit log ----------------
def log_turn(**fields):
    """Append an AI Chat Log entry. Never raises: logging must not break the user's request,
    but a failure is recorded in Error Log."""
    try:
        for k in ("tool_trace", "actions_proposed"):
            if k in fields and not isinstance(fields[k], str):
                fields[k] = json.dumps(fields[k], ensure_ascii=False, default=str, indent=1)[:60000]
        for k in ("question", "reply"):
            if fields.get(k):
                fields[k] = str(fields[k])[:20000]
        doc = frappe.get_doc({"doctype": "AI Chat Log", "user": frappe.session.user, **fields})
        doc.insert(ignore_permissions=True)
        return doc.name
    except Exception:
        frappe.log_error(title="AI Chat Log write failed")


def purge_old_logs():
    """Daily: delete AI Chat Log entries older than the retention period."""
    days = int(setting("log_retention_days", 365))
    cutoff = add_days(nowdate(), -days)
    frappe.db.delete("AI Chat Log", {"creation": ["<", cutoff]})
    frappe.db.commit()


def history(session_id: str, turns: int = 10) -> list:
    """Previous turns of this user's session, oldest first, as chat messages."""
    rows = frappe.get_all(
        "AI Chat Log",
        filters={"session_id": session_id, "user": frappe.session.user, "log_type": "Chat", "status": "Success"},
        fields=["question", "reply"], order_by="creation desc", limit_page_length=turns,
    )
    msgs = []
    for r in reversed(rows):
        msgs.append({"role": "user", "content": r.question or ""})
        msgs.append({"role": "assistant", "content": r.reply or ""})
    return msgs


def latest_session() -> str | None:
    row = frappe.get_all("AI Chat Log", filters={"user": frappe.session.user, "log_type": "Chat"},
                         fields=["session_id"], order_by="creation desc", limit_page_length=1)
    return row[0].session_id if row else None


# ---------------- limits ----------------
def _key(kind: str, bucket) -> str:
    return f"ai_recruitment:{frappe.local.site}:{kind}:{frappe.session.user}:{bucket}"


def _incr(key: str, by: int, ttl: int) -> int:
    n = frappe.cache.incrby(key, by)
    if n == by:
        frappe.cache.expire(key, ttl)
    return n


class RateLimited(Exception):
    pass


def check_and_count_message():
    per_min = int(setting("chat_per_minute", 10))
    per_day = int(setting("chat_per_day", 200))
    if _incr(_key("m", int(time.time() // 60)), 1, 120) > per_min:
        raise RateLimited(f"Limit of {per_min} messages per minute reached. Please wait a moment.")
    if _incr(_key("d", nowdate()), 1, 2 * 86400) > per_day:
        raise RateLimited(f"Daily limit of {per_day} messages reached.")
    used = int(frappe.cache.get(_key("t", nowdate())) or 0)
    if used >= int(setting("tokens_per_user_day", 300000)):
        raise RateLimited("Daily AI usage budget reached. It resets tomorrow.")


def count_tokens(n: int):
    if n:
        _incr(_key("t", nowdate()), int(n), 2 * 86400)
