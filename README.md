# Ministry Career Portal — AI Recruitment

**Ministry of Economy & Tourism.** AI CV screening and a recruitment assistant for Frappe HR v16.

## Repository layout
| Path | What |
|---|---|
| `frappe/apps/ai_recruitment/` | **The Frappe app**: background CV scoring, the in-Desk *Ask AI* assistant, AI Recruitment Settings. See its README. |
| `frappe/Dockerfile` | Demo image for Railway: ERPNext v16 + Frappe HR + this app. |
| `docs/ON_PREM.md` | **On-premises deployment guide**: build, run, LLM, security, migration. |

## How it works
```
Candidate ─► /jobs ─► Job Applicant ─after_insert─► background job ─► LLM ─► score + evidence on the record
HR user ─► Desk "Ask AI" ─► ai_recruitment.api.chat (user's session & permissions) ─► LLM tool-calling
                                         └─ writes are proposals; the user clicks Approve to apply
```
- The LLM is any OpenAI-compatible endpoint, configured in **AI Recruitment Settings**. The demo uses
  `qwen/qwen3.8-27b` on OpenRouter. On-prem, point it at your vLLM server.
- Each job's rubric goes in **Job Opening → AI Scoring Criteria**, one per line: `weight | criterion | must`.
- AI scores are recommendations only. Reject, accept and delete are never done by the assistant.

## Demo (Railway)
The `Frappe HR` service builds `frappe/Dockerfile` (root directory `/frappe`). Pushing to `frappe/**` on
`main` redeploys it. The entrypoint backs up the database and runs `bench migrate` whenever an app version changes,
so bump `ai_recruitment/__init__.py` when you change the app.
