"""MCP tools for the HR chatbot. Narrow, recruitment-only, no delete/submit.

Every write needs confirm=true: the first call returns a preview the agent must
show the user, and only a second call with confirm=true performs the change.
Calls run as the least-privilege service user, so Frappe role permissions apply.
"""
from typing import Literal

from mcp.server.fastmcp import FastMCP

from .scoring import score_applicant
from .state import state

mcp = FastMCP(
    "ministry-recruitment",
    instructions=(
        "Recruitment assistant tools for the ministry's Frappe HR. AI scores are recommendations; "
        "humans make hiring decisions. Always show the preview of a write and get the user's "
        "explicit approval before calling it again with confirm=true."
    ),
    host="0.0.0.0",
    stateless_http=True,
    json_response=True,
)

JA_FIELDS = ["name", "applicant_name", "job_title", "status", "ai_score", "ai_status", "ai_must_have_gaps"]


def _svc():
    if not state.ready or not state.svc:
        raise RuntimeError(f"Service not ready yet (phase: {state.phase})")
    return state.svc


@mcp.tool()
async def list_job_openings(status: Literal["Open", "Closed"] = "Open") -> list[dict]:
    """List job openings with their status and designation."""
    return await _svc().get_list(
        "Job Opening", fields=["name", "job_title", "designation", "status"],
        filters=[["status", "=", status]], order_by="creation desc", limit=50,
    )


@mcp.tool()
async def pipeline_status(job_opening: str) -> dict:
    """Applicant counts per status and AI scoring progress for one job opening (use its ID)."""
    rows = await _svc().get_list("Job Applicant", fields=JA_FIELDS,
                                 filters=[["job_title", "=", job_opening]], limit=1000)
    by_status: dict[str, int] = {}
    by_ai: dict[str, int] = {}
    scores = []
    for r in rows:
        by_status[r.get("status") or "Open"] = by_status.get(r.get("status") or "Open", 0) + 1
        by_ai[r.get("ai_status") or "Pending"] = by_ai.get(r.get("ai_status") or "Pending", 0) + 1
        if r.get("ai_status") == "Scored":
            scores.append(r.get("ai_score") or 0)
    return {
        "job_opening": job_opening,
        "total_applicants": len(rows),
        "by_status": by_status,
        "by_ai_status": by_ai,
        "average_ai_score": round(sum(scores) / len(scores), 1) if scores else None,
    }


@mcp.tool()
async def shortlist_applicants(job_opening: str, top_n: int = 5, min_score: float = 0) -> list[dict]:
    """Top applicants for a job opening ranked by AI score, with must-have gaps. Ranking only; no status change."""
    rows = await _svc().get_list(
        "Job Applicant", fields=JA_FIELDS,
        filters=[["job_title", "=", job_opening], ["ai_status", "=", "Scored"],
                 ["ai_score", ">=", min_score], ["status", "not in", ["Rejected"]]],
        order_by="ai_score desc", limit=max(1, min(top_n, 50)),
    )
    return rows


@mcp.tool()
async def get_applicant_summary(applicant: str) -> dict:
    """AI summary, per-criterion reasoning and current status for one applicant (use its ID)."""
    d = await _svc().get_doc("Job Applicant", applicant)
    return {k: d.get(k) for k in (
        "name", "applicant_name", "job_title", "status", "ai_score", "ai_status",
        "ai_summary", "ai_reasoning", "ai_must_have_gaps", "ai_model", "ai_scored_on",
    )}


@mcp.tool()
async def rescore_applicant(applicant: str) -> dict:
    """Re-run AI scoring for one applicant, e.g. after HR edits the job's scoring criteria."""
    _svc()
    return await score_applicant(applicant)


@mcp.tool()
async def set_applicant_status(
    applicant: str, status: Literal["Open", "Hold", "Replied"], confirm: bool = False
) -> str:
    """Move an applicant to Open, Hold or Replied. Rejecting or accepting is done by a human in Frappe.
    Call first with confirm=false to get a preview; call again with confirm=true after the user approves."""
    d = await _svc().get_doc("Job Applicant", applicant)
    preview = f"Change {d.get('applicant_name')} ({applicant}) from '{d.get('status')}' to '{status}'."
    if not confirm:
        return "PREVIEW — not applied. " + preview + " Ask the user to confirm, then call again with confirm=true."
    await _svc().update("Job Applicant", applicant, {"status": status})
    return "Done. " + preview


@mcp.tool()
async def list_interview_rounds() -> list[dict]:
    """Interview rounds available for scheduling."""
    return await _svc().get_list("Interview Round", fields=["name"], limit=50)


@mcp.tool()
async def schedule_interview(
    applicant: str, interview_round: str, date: str, from_time: str, to_time: str, confirm: bool = False
) -> str:
    """Create a draft interview. date as YYYY-MM-DD, times as HH:MM (24h, UAE time).
    Call first with confirm=false for a preview; call again with confirm=true after the user approves."""
    d = await _svc().get_doc("Job Applicant", applicant)
    preview = (f"Schedule '{interview_round}' for {d.get('applicant_name')} ({applicant}) "
               f"on {date} {from_time}-{to_time}.")
    if not confirm:
        return "PREVIEW — not applied. " + preview + " Ask the user to confirm, then call again with confirm=true."
    doc = await _svc().insert({
        "doctype": "Interview",
        "interview_round": interview_round,
        "job_applicant": applicant,
        "scheduled_on": date,
        "from_time": from_time if len(from_time) > 5 else f"{from_time}:00",
        "to_time": to_time if len(to_time) > 5 else f"{to_time}:00",
        "status": "Pending",
    })
    return f"Done. Draft interview {doc['name']} created. " + preview
