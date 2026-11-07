# AI Recruitment (Frappe app)

By the **Ministry of Economy & Tourism**. AI CV screening and a recruitment assistant for Frappe HR v16.

## Features
- **Automatic CV scoring.** Each new Job Applicant is scored in a background job against the job's rubric
  (*AI Scoring Criteria*: `weight | criterion | must`). Before anything is sent to the model, contacts, the
  name and labelled personal attributes are redacted. The weighted score is computed in code. It writes per-criterion
  evidence, must-have gaps and an English + Arabic summary, plus an audit comment.
- **Any CV, Arabic or English, ATS-style or not.** ATS-style PDFs use the text layer. Designed or multi-column
  PDFs, scans and images are read by the vision model, with Tesseract (Arabic + English) as fallback. DOCX tables
  and text boxes are included. Every CV is parsed into a JSON Resume shaped profile with computed years of
  experience, which can be downloaded as JSON Resume. **AI OCR Test** compares OCR engines on your own sample CVs.
- **Recruitment Assistant.** The **Ask AI** panel appears on every Desk page for allowed roles. Replies stream live
  and render Markdown, and the panel is translated into Arabic with RTL support. It calls whitelisted methods
  with the user's own session, so every tool obeys that user's permissions. It can:
  - search applicants by skill, employer or name
  - shortlist and compare candidates
  - summarise applicants and interview feedback
  - read job openings
  - find free interview slots from interviewers' interviews and calendar events
- **Human approval for every change.** Changing a status (Open, Hold or Replied), scheduling an interview with an
  `.ics` invite, creating a job opening, updating a rubric and emailing a candidate are only *proposed*. They run
  after the same user clicks **Approve**. Proposals are single-use and expire after 30 minutes.
- **Governance.**
  - **AI Chat Log** is an append-only audit of every turn and approval: question, reply, tool calls,
    proposals, tokens and latency. It has a retention period, and a daily purge removes older entries.
  - Each user has limits on messages per minute and per day, and on tokens per day.
  - Conversations are kept server-side per session.
- **AI Parsing Case / AI Parsing Run.** Hand-labelled CVs and an accuracy report (years, roles, skills,
  grounding, language), overall and per language and layout.
- **AI Evaluation Run.** Tests consistency, fairness (name, unlabelled demographics, labelled attributes) and
  prompt-injection resistance on a job's applicants, and produces a PASS/FAIL report.
- **AI Recruitment Settings.** Covers the LLM endpoint and model, the encrypted API key, roles, limits, retention, the
  evaluation threshold, OCR mode, interview invites and working hours. It works with any OpenAI-compatible server
  (vLLM, Ollama, LiteLLM, OpenRouter).

## Install
```bash
bench get-app <git-url-of-this-app>
bench --site <site> install-app ai_recruitment
```
Requires `hrms`. Custom fields and settings defaults are applied on install and on every `bench migrate`.
For OCR install `tesseract-ocr tesseract-ocr-ara tesseract-ocr-eng poppler-utils` on the server or image.

## Whitelisted API
| Method | Who | Purpose |
|---|---|---|
| `ai_recruitment.api.chat` | allowed roles | assistant turn: `{message, session_id, context, request_id}` |
| `ai_recruitment.api.history` | allowed roles | the user's own latest (or given) session |
| `ai_recruitment.api.approve` | the proposing user | run a proposed action by id |
| `ai_recruitment.api.rescore` | write access on the applicant | queue re-scoring |
| `ai_recruitment.api.rescore_unscored` | HR Manager, System Manager | queue all Pending/Failed |

## Tests
```bash
python -m unittest discover -s tests      # pure helpers, no Frappe needed
node --test tests/js/*.test.js            # markdown renderer safety
bench --site test_site run-tests --app ai_recruitment   # integration, mocked LLM
```

See `docs/ON_PREM.md` in the repository root for production deployment.

## License
MIT
