# AI Recruitment (Frappe app)

By the **Ministry of Economy & Tourism**. It adds AI CV screening and a recruitment assistant to Frappe HR v16.

## Features
- **Automatic CV scoring.** Each new Job Applicant is scored in a background job (`long` queue) against
  the job's rubric (*AI Scoring Criteria*: `weight | criterion | must`). It redacts personal attributes
  first and computes the weighted score in code. It writes per-criterion evidence, must-have gaps and an
  English + Arabic summary to the applicant, plus an audit comment.
- **Recruitment Assistant.** An **Ask AI** panel on every Desk page for allowed roles, with Markdown replies.
  It calls whitelisted methods with the user's own session, so tools obey that user's permissions. Status
  changes and interviews are only *proposed*, and run after the same user clicks **Approve**.
- **AI Recruitment Settings.** LLM base URL, model, encrypted API key, allowed roles and limits.
  It works with any OpenAI-compatible server (vLLM, Ollama, LiteLLM, OpenRouter).

## Install
```bash
bench get-app <git-url-of-this-app>
bench --site <site> install-app ai_recruitment
```
Requires `hrms`. Custom fields are created or updated on install and on every `bench migrate`.

## Whitelisted API
| Method | Who | Purpose |
|---|---|---|
| `ai_recruitment.api.chat` | allowed roles | assistant turn: `{messages, context}` → `{reply, actions}` |
| `ai_recruitment.api.approve` | the proposing user | run a proposed action by id |
| `ai_recruitment.api.rescore` | write access on applicant | queue re-scoring |
| `ai_recruitment.api.rescore_unscored` | HR Manager, System Manager | queue all Pending/Failed |

See `docs/ON_PREM.md` in the repository root for production deployment.

## License
MIT
