# Ministry Career Portal — AI

Ministry of Economy & Tourism. AI CV screening and a recruitment assistant for Frappe HR.

## Repository layout
| Path | What |
|---|---|
| `frappe/apps/ai_recruitment/` | **The Frappe app**: scoring, in-Desk assistant, settings. This is the main product. |
| `frappe/Dockerfile` | Demo image for Railway (Frappe HR + the app). On-prem uses frappe_docker instead. |
| `app/`, `Dockerfile` (root) | **AI service**: optional MCP gateway for external agents (`MANAGE_FRAPPE=false`). |
| `docs/ON_PREM.md` | **On-premises deployment guide.** |

---

# AI Service (MCP gateway)

A small, portable service that exposes **HR recruitment tools over MCP** to outside agents, such as the
ministry's agentic pipeline. With `MANAGE_FRAPPE=true` (the original demo mode) it also scored CVs via
webhook. That is now done natively by the `ai_recruitment` Frappe app, so production runs it with
`MANAGE_FRAPPE=false`. It's a plain Docker container that talks to Frappe over its REST API and to
any OpenAI-compatible LLM. It has no dependency on any hosting platform.

```
Candidate applies on /jobs ──► Frappe HR ──webhook──► AI service ──► LLM (OpenRouter / vLLM / Ollama)
                                   ▲                       │
                                   └── score + reasoning ◄─┘
HR chatbot / agent ──MCP──► AI service ──REST (HR User role)──► Frappe HR
```

## What it does
- **Bootstrap (idempotent):** creates AI fields on Job Applicant and Job Opening, a least-privilege service user
  (`HR User` role), and an `after_insert` webhook. Optionally seeds demo jobs with fictional CVs.
- **Scoring:** extracts text from the PDF/DOCX/TXT CV and cover letter. It redacts name, email, phone, nationality,
  age, gender and religion lines, then scores against the job's rubric. The weighted total is **computed in code**,
  not by the model. It writes the score, the per-criterion evidence and an English + Arabic summary back to the
  applicant, and adds an audit comment.
- **MCP tools** (`POST /mcp`, Bearer `MCP_TOKEN`): `list_job_openings`, `pipeline_status`, `shortlist_applicants`,
  `get_applicant_summary`, `rescore_applicant`, `set_applicant_status` (Open/Hold/Replied only),
  `list_interview_rounds`, `schedule_interview` (draft). Writes require `confirm=true`. There are no delete,
  reject or submit tools, because humans decide.

## Rubric format (Job Opening → "AI Scoring Criteria")
```
30 | 3+ years building production Python services | must
25 | Hands-on LLM work: RAG, agents, tool calling | must
15 | Model serving / MLOps
```
If the rubric is empty, the model derives 4–6 criteria from the job description.

## Configuration
See `.env.example`. Switching from the demo to on-prem only needs a change of environment variables:
```
LLM_BASE_URL=http://vllm.internal:8000/v1
LLM_MODEL=Qwen/Qwen3.8-27B
LLM_API_KEY=            # empty if your gateway needs none
```

## Run anywhere
```bash
docker build -t ministry-ai-service .
docker run --env-file .env -p 8000:8000 ministry-ai-service
curl localhost:8000/health
```

## Endpoints
| Method | Path | Auth | Purpose |
|---|---|---|---|
| GET | `/health` | — | status, bootstrap phase, LLM configured |
| POST | `/webhook/job-applicant` | `X-Webhook-Token` | called by Frappe on new applicant |
| POST | `/score/{applicant}` | Bearer `MCP_TOKEN` | re-score one applicant |
| POST | `/rescore-pending` | Bearer `MCP_TOKEN` | queue all unscored/failed |
| POST | `/mcp` | Bearer `MCP_TOKEN` | MCP (streamable HTTP) |

## Connecting an MCP client
```json
{ "mcpServers": { "recruitment": {
    "url": "https://<service>/mcp",
    "headers": { "Authorization": "Bearer <MCP_TOKEN>" } } } }
```

## Production notes
- Use `FRAPPE_API_KEY/SECRET` (Option B) and drop `FRAPPE_ADMIN_PASSWORD` once bootstrapped.
- For per-user accountability in the chatbot, pass each HR user's own Frappe token instead of the shared service user.
- Keep candidate data in-country: point `LLM_BASE_URL` at your on-prem model, not a hosted gateway.
- Scanned CVs need OCR (e.g. add Tesseract) before scoring; they are marked `Failed` with a reason today.
