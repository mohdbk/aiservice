# On-Premises Deployment Guide

**Ministry of Economy & Tourism: AI-enabled Career Portal (Frappe HR + `ai_recruitment`)**

This guide covers what you need to move the Railway demo to ministry infrastructure.
Nothing in the production setup depends on Railway or OpenRouter.

---

## 1. What you are deploying

| Component | What it is | Required? |
|---|---|---|
| **Frappe HR site** | Frappe Framework v16 + ERPNext v16 + Frappe HR (`hrms`) v16, with the public careers page at `/jobs` | Yes |
| **`ai_recruitment` app** | Our Frappe app in `frappe/apps/ai_recruitment`: CV scoring, in-Desk assistant, settings | Yes |
| **MariaDB 10.6+ / 11.x** | Database | Yes |
| **Redis** | Cache, queue, realtime (frappe_docker runs 2 instances) | Yes |
| **LLM endpoint** | Any OpenAI-compatible server (vLLM recommended) | Yes |

There are no other services. Scoring and the assistant run inside Frappe's own web and worker processes.

```
Candidate ──► /jobs (Frappe web) ──► Job Applicant ──after_insert──► background job (long queue)
                                                                      │
HR user ──► Desk "Ask AI" panel ──► ai_recruitment.api.chat (session) ─┤
                                                                      ▼
                                                     LLM endpoint (on-prem vLLM)
```

### Versions pinned in this repo
| Item | Version | Where |
|---|---|---|
| ERPNext base image | `frappe/erpnext:v16.54.0` | `frappe/Dockerfile` (`ERPNEXT_IMAGE`) |
| Frappe HR | `v16.53.1` | `frappe/Dockerfile` (`HRMS_VERSION`) |
| ai_recruitment | `0.1.2` | `frappe/apps/ai_recruitment/ai_recruitment/__init__.py` |
| Demo LLM | `qwen/qwen3.8-27b` (OpenRouter) | AI Recruitment Settings |

Upgrade them deliberately and test in staging first. Every new image version runs `bench migrate`.

---

## 2. Build the production image (frappe_docker)

Production should use **[frappe_docker](https://github.com/frappe/frappe_docker)** (MIT), Frappe's official
container setup, not the Railway Dockerfile in `frappe/`. The Railway one bundles everything into one
container to suit Railway's single-volume limit, and pulls third-party Railway scripts.

1. Publish `frappe/apps/ai_recruitment` as its own git repo on your internal Git server (GitLab, Gitea or
   Azure DevOps), e.g. `https://git.moet.internal/digital/ai_recruitment.git`.
2. Create `apps.json`:
   ```json
   [
     {"url": "https://github.com/frappe/erpnext", "branch": "v16.54.0"},
     {"url": "https://github.com/frappe/hrms", "branch": "v16.53.1"},
     {"url": "https://git.moet.internal/digital/ai_recruitment.git", "branch": "main"}
   ]
   ```
3. Build with frappe_docker's layered Containerfile:
   ```bash
   git clone https://github.com/frappe/frappe_docker && cd frappe_docker
   export APPS_JSON_BASE64=$(base64 -w 0 /path/to/apps.json)
   docker build \
     --build-arg=FRAPPE_PATH=https://github.com/frappe/frappe \
     --build-arg=FRAPPE_BRANCH=v16.x.x \
     --build-arg=APPS_JSON_BASE64=$APPS_JSON_BASE64 \
     --tag=registry.moet.internal/hr/frappe-hr-ai:0.1.0 \
     --file=images/layered/Containerfile .
   docker push registry.moet.internal/hr/frappe-hr-ai:0.1.0
   ```
   Check the frappe_docker README for the exact build arguments of the release you use. They change
   between versions. A private Git server needs credentials in the URL or a build secret. Never bake
   tokens into the image.
4. **Air-gapped builds:** mirror GitHub (frappe, erpnext, hrms), PyPI (`pypdf`, `python-docx` plus Frappe's
   own dependencies) and npm (Frappe's asset build) internally, or build on a connected machine and
   import the image.

---

## 3. Run it

Use frappe_docker's `compose.yaml` with overrides for MariaDB and Redis (`overrides/compose.mariadb.yaml`,
`overrides/compose.redis.yaml`), or the Helm chart [frappe/helm](https://github.com/frappe/helm) on Kubernetes.
Set `CUSTOM_IMAGE` / `CUSTOM_TAG` to the image above.

Services in a standard deployment: `backend` (gunicorn), `frontend` (nginx), `websocket` (socket.io),
`queue-short`, **`queue-long`** (CV scoring runs here, so keep at least one worker), `scheduler`,
`db`, `redis-cache`, `redis-queue`.

Create the site (first time):
```bash
docker compose exec backend bench new-site hr.moet.internal \
  --mariadb-user-host-login-scope=% --admin-password '<strong-password>' \
  --install-app erpnext --install-app hrms --install-app ai_recruitment --set-default
docker compose exec backend bench --site hr.moet.internal enable-scheduler
```
Then complete the setup wizard (UAE, AED, Asia/Dubai, company).

Upgrades: build a new image tag, update `CUSTOM_TAG`, `docker compose up -d`, then
`docker compose exec backend bench --site hr.moet.internal migrate`. Back up first (section 7).

---

## 4. Configure the AI

**Desk → AI Recruitment Settings**
| Field | On-prem value |
|---|---|
| Base URL | e.g. `http://vllm.ai.moet.internal:8000/v1` |
| Model | the served model name, e.g. `Qwen/Qwen3.8-27B` |
| API Key | blank, or your gateway key (stored encrypted in Frappe) |
| Auto-score New Applicants | on |
| Roles Allowed | `HR User`, `HR Manager`, `System Manager` (one per line) |

Alternatively set environment variables on the backend and worker containers: `LLM_BASE_URL`,
`LLM_MODEL`, `LLM_API_KEY`. Values in the Settings page take precedence.

### LLM server requirements
- OpenAI-compatible `/v1/chat/completions` with **tool calling** (assistant) and **JSON schema / JSON mode**
  (scoring). With vLLM, enable tool calling (`--enable-auto-tool-choice --tool-call-parser <parser for your model>`).
  Guided JSON is built in.
- Context of 32k tokens or more is enough. CVs are truncated to *Max CV Characters* (default 40,000).
- Sizing guide for a 27B dense model: one 80 GB GPU (FP16/BF16), or one 48 GB GPU with FP8/AWQ quantisation.
  Expect about 5–15 s per CV.
- The app retries without `json_schema` / `reasoning` if the server rejects them, so most servers work.

---

## 5. Network and security checklist

- [ ] **Data residency:** the LLM endpoint is in-country or on-prem. CVs are redacted of name,
      contacts, nationality, age, gender and religion before sending, but they are still personal data.
- [ ] The Frappe backend and worker containers can reach the LLM endpoint. No other egress is needed at runtime.
- [ ] TLS on the public site (reverse proxy or load balancer). Only `/jobs`, `/api/method/...` for the web
      form, and the assets need to be internet-facing. Desk (`/app`) can be restricted to the ministry network or VPN.
- [ ] Administrator password changed after setup. Set up SSO for staff (UAE PASS or the ministry's Azure AD
      via Frappe's Social Login Keys / OAuth).
- [ ] **Assistant permissions:** each chat tool runs as the logged-in user with normal Frappe permissions.
      Writes (status Open/Hold/Replied, draft interviews) need an **Approve** click by the same user. Proposals
      are single-use and expire after 30 minutes. Reject, accept and delete are never exposed.
- [ ] **Audit:** scoring and approved actions are written as comments on the Job Applicant. Field changes are
      in Frappe's Version log. Scoring errors are in *Error Log*.
- [ ] **AI governance:** scores are recommendations. Keep a human decision step in your recruitment SOP, and
      document the rubric per job opening (the *AI Scoring Criteria* field).
- [ ] The LLM API key (if any) is in AI Recruitment Settings (encrypted with the site's `encryption_key`) or
      in an `LLM_API_KEY` environment variable from your secrets store. Never commit it.

---

## 6. Migrating from the Railway demo

The demo holds fictional data only. A fresh site is cleaner:
1. Recreate job openings and rubrics (or export them with Data Export and import them).
2. **Don't migrate** demo applicants.

If you do need to move a site:
```bash
# On Railway (railway ssh into the Frappe HR service)
erpnext-backup                       # or: bench --site <site> backup --with-files
# Copy the database .sql.gz and files tars, then on-prem:
bench --site hr.moet.internal restore <db.sql.gz> --with-public-files <pub.tar> --with-private-files <priv.tar>
bench --site hr.moet.internal migrate
```
Copy `encryption_key` from the old `site_config.json` so saved passwords, including the LLM API key, keep working.

---

## 7. Backups and monitoring

- Nightly `bench --site <site> backup --with-files` to ministry backup storage. Test a restore quarterly.
- Monitor queue length for the `long` queue (Desk → RQ Jobs), *Error Log* entries titled "AI scoring failed",
  and LLM endpoint latency.
- Bulk re-score after a rubric change: as an HR Manager, call `ai_recruitment.api.rescore_unscored`
  (optionally with `job_opening`), or use **Re-score with AI** on each applicant.

---

## 8. Known limitations and next steps

- **Scanned (image-only) PDFs** are marked *Failed: no readable text*. Add OCR (Tesseract or your vision
  model) in `scoring.get_cv_text` if needed.
- `resume_link` (external URLs) is intentionally not fetched, to avoid server-side requests to arbitrary sites.
- The careers page is Frappe HR's standard `/jobs`. Arabic/RTL presentation, ministry branding and UAE PASS
  for candidates are separate work items.
- The demo's Railway setup runs everything in one container with no high availability. On-prem should run the multi-service
  compose or Helm deployment above.

---

## 9. House style

AI output must never contain em dashes. The rule is in both system prompts (scoring and assistant), and
`llm.no_em_dash` / `llm.clean` remove any that slip through from every model reply before it is saved or
shown. Keep this when changing models or prompts. The repo's own text follows the same rule.
