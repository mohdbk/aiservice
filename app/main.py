import asyncio
import hmac
import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

from .bootstrap import bootstrap_loop
from .config import settings
from .mcp_tools import mcp
from .scoring import scoring_worker
from .state import state

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")

mcp_app = mcp.streamable_http_app()  # serves POST /mcp


@asynccontextmanager
async def lifespan(app: FastAPI):
    tasks = [asyncio.create_task(scoring_worker()), asyncio.create_task(bootstrap_loop())]
    async with mcp.session_manager.run():
        yield
    for t in tasks:
        t.cancel()


app = FastAPI(title="Ministry Career Portal — AI Service", lifespan=lifespan)


def _token_ok(given: str | None, expected: str) -> bool:
    return bool(expected) and bool(given) and hmac.compare_digest(given, expected)


@app.middleware("http")
async def mcp_auth(request: Request, call_next):
    if request.url.path.startswith("/mcp"):
        auth = request.headers.get("authorization", "")
        if not _token_ok(auth.removeprefix("Bearer ").strip(), settings.mcp_token):
            return JSONResponse({"error": "unauthorized"}, status_code=401)
    return await call_next(request)


@app.get("/health")
async def health():
    return {
        "status": "ok",
        "ready": state.ready,
        "phase": state.phase,
        "llm_configured": settings.llm_configured,
        "llm_model": settings.llm_model,
        "queue": state.queue.qsize(),
        "last_error": state.last_error[:300],
    }


@app.post("/webhook/job-applicant")
async def job_applicant_webhook(request: Request):
    if not _token_ok(request.headers.get("x-webhook-token"), settings.webhook_token):
        return JSONResponse({"error": "unauthorized"}, status_code=401)
    body = await request.json()
    name = (body or {}).get("name")
    if not name:
        return JSONResponse({"error": "missing name"}, status_code=400)
    state.enqueue(name)
    return {"queued": name}


@app.post("/score/{applicant}")
async def rescore(applicant: str, request: Request):
    auth = request.headers.get("authorization", "").removeprefix("Bearer ").strip()
    if not _token_ok(auth, settings.mcp_token):
        return JSONResponse({"error": "unauthorized"}, status_code=401)
    state.enqueue(applicant)
    return {"queued": applicant}


@app.post("/rescore-pending")
async def rescore_pending(request: Request):
    auth = request.headers.get("authorization", "").removeprefix("Bearer ").strip()
    if not _token_ok(auth, settings.mcp_token):
        return JSONResponse({"error": "unauthorized"}, status_code=401)
    from .bootstrap import queue_unscored
    if not state.ready:
        return JSONResponse({"error": f"not ready ({state.phase})"}, status_code=503)
    await queue_unscored(state.svc)
    return {"queued": state.queue.qsize()}


# MCP last, so the routes above take precedence.
app.mount("/", mcp_app)
