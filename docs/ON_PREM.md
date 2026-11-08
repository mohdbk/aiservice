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
| **Socket.IO (websocket)** | Streams the assistant's replies and status to the browser | Yes (replies still arrive without it, just not live) |
| **PaddleOCR PP-OCRv5** | OCR draft for scanned/photo CVs (own Python 3.12 environment in the image) | Yes |
| **poppler-utils** | Renders PDF pages to images for OCR (OS package) | Yes |
| **Outgoing email account** | Interview invites and candidate emails sent from the assistant | For those features |
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
| ai_recruitment | `0.6.0` | `frappe/apps/ai_recruitment/ai_recruitment/__init__.py` |
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
     --tag=registry.moet.internal/hr/frappe-hr-ai-base:0.6.0 \
     --file=images/layered/Containerfile .
   ```
4. Add PaddleOCR on top (frappe_docker images run as the `frappe` user). Copy the PaddleOCR block from
   `frappe/Dockerfile` in this repo; in short:
   ```dockerfile
   FROM registry.moet.internal/hr/frappe-hr-ai-base:0.6.0
   USER root
   RUN apt-get update && apt-get install -y --no-install-recommends \
         poppler-utils libgl1 libglib2.0-0 libgomp1 \
       && rm -rf /var/lib/apt/lists/*
   # PaddleOCR needs Python 3.8-3.12, so it gets its own environment (Frappe v16 runs a newer Python)
   ENV PADDLE_PDX_CACHE_HOME=/home/frappe/.paddlex
   RUN python3 -m venv /opt/uvtool && /opt/uvtool/bin/pip install uv \
    && UV_PYTHON_INSTALL_DIR=/opt/python /opt/uvtool/bin/uv venv --python 3.12 /home/frappe/paddleocr-env \
    && /opt/uvtool/bin/uv pip install --python /home/frappe/paddleocr-env/bin/python \
         --index-strategy unsafe-best-match --extra-index-url https://www.paddlepaddle.org.cn/packages/stable/cpu/ \
         "paddlepaddle>=3.2.1,<4" "paddleocr>=3.3,<4" \
    && /home/frappe/paddleocr-env/bin/python -c "import sys; sys.path.insert(0,'/home/frappe/frappe-bench/apps/ai_recruitment/ai_recruitment'); import paddle_runner as r; r._engine('ar'); r._engine('en')" \
    && chown -R "$(id -u frappe):$(id -g frappe)" /home/frappe/paddleocr-env /home/frappe/.paddlex && touch /home/frappe/paddleocr-env/.ready
   USER frappe
   RUN bench compile-po-to-mo --app ai_recruitment || true   # Arabic UI strings
   ```
   With a GPU on the Frappe workers, install `paddlepaddle-gpu` instead and set `PADDLEOCR_DEVICE=gpu:0`.
   Air-gapped: pre-download the models (`PP-OCRv5_mobile_det`, `arabic_PP-OCRv5_mobile_rec`,
   `en_PP-OCRv5_mobile_rec`) into `PADDLE_PDX_CACHE_HOME`.
   ```bash
   docker build -t registry.moet.internal/hr/frappe-hr-ai:0.6.0 . && docker push registry.moet.internal/hr/frappe-hr-ai:0.6.0
   ```
   Check the frappe_docker README for the exact build arguments of the release you use. They change
   between versions. A private Git server needs credentials in the URL or a build secret. Never bake
   tokens into the image.
5. **Air-gapped builds:** mirror GitHub (frappe, erpnext, hrms), PyPI (`pypdf`, `python-docx` plus Frappe's
   own dependencies) and npm (Frappe's asset build) internally, or build on a connected machine and
   import the image.

---

## 3. Run it

Use frappe_docker's `compose.yaml` with overrides for MariaDB and Redis (`overrides/compose.mariadb.yaml`,
`overrides/compose.redis.yaml`), or the Helm chart [frappe/helm](https://github.com/frappe/helm) on Kubernetes.
Set `CUSTOM_IMAGE` / `CUSTOM_TAG` to the image above.

Services in a standard deployment: `backend` (gunicorn), `frontend` (nginx), `websocket` (socket.io),
`queue-short`, **`queue-long`** (CV scoring and evaluation runs, so keep at least one worker), **`scheduler`**
(daily chat-log retention purge), `db`, `redis-cache`, `redis-queue`. The **`websocket`** service must be reachable
through the same hostname as the site (frappe_docker's nginx already routes `/socket.io`).

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
| Messages per Minute / per Day | 10 / 200 per user |
| Tokens per Day | 300,000 per user (prompt + completion) |
| Keep Chat Logs (days) | per the ministry's records retention policy (default 365) |
| Evaluation Threshold | 5 points (max allowed score change in evaluation runs) |
| CV Reading Mode | `Auto` (PaddleOCR + vision model, see section 4a) or `Off` |
| Send Interview Invites | on, once an outgoing Email Account is configured |
| Working Day Starts / Ends | 09:00 / 17:00 (used to suggest free interview slots) |

Also:
- Give interviewers the **Interviewer** role. The assistant uses it to list interviewers and check their
  interviews and calendar Events for conflicts.
- Configure a default outgoing **Email Account** for interview invites (`.ics` attached) and candidate emails.

Alternatively set environment variables on the backend and worker containers: `LLM_BASE_URL`,
`LLM_MODEL`, `LLM_API_KEY`. Values in the Settings page take precedence.

## 4a. Reading CVs: Arabic and English, ATS and non-ATS

### OCR benchmark result (November 2026)

`benchmark/ocr` compares engines on 20 fictional CV pages with exact ground truth: 2 English, 2 Arabic, 1 mixed,
each as ATS, designed two-column, scanned and phone photo. Content accuracy is word F1 (0 to 1). Times were measured
on the demo's 8 vCPU / 8 GB server with no GPU.

| Engine | Overall | Arabic | Mixed | Photos | Time per page |
|---|---|---|---|---|---|
| **PaddleOCR + Qwen3.8 hybrid (Auto)** | **0.993** | **0.986** | **0.992** | **0.982** | ~4 s model + ~14 s CPU OCR |
| Tesseract + Qwen3.8 hybrid | 0.987 | 0.985 | 0.964 | 0.958 | ~4.5 s |
| Qwen3.8 vision only | 0.979 | 0.964 | 0.966 | 0.955 | ~4 s |
| PaddleOCR PP-OCRv5 only | 0.892 | 0.854 | 0.787 | 0.693 | ~14 s CPU |
| Tesseract only | 0.818 | 0.711 | 0.770 | 0.552 | ~1 s |
| PaddleOCR-VL 1.6 | not measurable: out of memory during model load on 8 GB CPU | | | | |

**Decision (8 November 2026):** only the PaddleOCR + vision-model hybrid is kept. Tesseract and the other
modes were removed from the app. If one half is unavailable, the other is used alone: the vision model reads the page
images directly, or PaddleOCR's text is used as is. A good draft matters: with a broken draft the hybrid did worse than the vision model
alone, which matches published findings on Arabic OCR correction.

**PaddleOCR-VL** is promising (strong vendor-reported Arabic results) but needs a GPU (about 7.5 GB VRAM at 16-bit).
On-prem, serve it on a GPU and compare it with **AI OCR Test** before considering a switch.

Notes from the benchmark that matter for deployment:
- PaddlePaddle 3.3 on CPU fails with `ConvertPirAttribute2RuntimeAttribute not support` when oneDNN is on, so the
  runner disables it (`PADDLEOCR_ENABLE_MKLDNN=1` turns it back on once fixed upstream).
- PaddleOCR ignores `lang` once any model name is set. The runner names the Arabic and English recognition models
  explicitly. Without that, Arabic CVs are read with the Chinese/English model (F1 about 0.1).
- PaddleOCR-VL drops "aside" (sidebar), header and footer text from its Markdown by default
  (`markdown_ignore_labels`). Set it to an empty list for CVs.
- These are synthetic pages in three fonts. Confirm on 10 to 20 real CVs with **AI OCR Test**, which reports the
  accuracy of the PaddleOCR draft and of the final corrected text.

Every CV, whatever its format, becomes the same structured profile (JSON Resume v1 shape: work with dates,
education, skills, languages, certificates, projects, plus computed years of experience). Scoring uses that profile
and the text, so layout does not decide the score.

| CV type | How it is read in `Auto` mode |
|---|---|
| ATS-style PDF (single column, real text) | PDF text layer |
| Designed / multi-column PDF (sidebars, columns) | detected from text positions; the text layer (exact characters, scrambled order) is the draft and the **vision model** rewrites it in reading order from the page images |
| Scanned PDF, photo or image CV | **PaddleOCR** draft corrected by the **vision model** |
| DOCX, including tables and text boxes | DOCX XML in document order |

The method and layout used are saved on each applicant (*CV Read As*). The parsed profile is in *Structured
Profile* and can be downloaded as JSON Resume (**AI → Download JSON Resume** on the applicant). JSON Resume
is an open schema ([jsonresume.org](https://jsonresume.org/schema)). It is not a formal certification, and
HR Open Standards mapping can be added later if a system you integrate with needs it.

**Background (literature):** on KITAB-Bench, an Arabic OCR benchmark ([paper](https://arxiv.org/html/2502.14949v2)),
Tesseract had about 0.54 character and 0.84 word error rate on Arabic text recognition and about 28% on tables, while
vision-language models reduced character errors by roughly 60% on average. Small older Qwen-VL 7B models did worse than
Tesseract there, so **measure on your own CVs**: Desk → **AI OCR Test** → upload a sample CV and paste its correct text.
It reports accuracy for the PaddleOCR draft and the final text. Test 10 to 20 real Arabic and English CVs before go-live.

### Measuring parsing accuracy

Prompt rules reduce parsing mistakes but cannot guarantee them, so measure:

1. Desk → **AI Parsing Case** → New, for 10 to 20 real CVs (anonymised if possible) covering Arabic, English,
   ATS-style, designed and scanned layouts. For each, attach the CV and record the correct answers: total years,
   roles (`position | employer`, employer empty when the CV names none) and the key skills actually used.
2. Desk → **AI Parsing Run** → New → Save. It parses every active case and reports, overall and per language
   and layout:

| Metric | Meaning | Default minimum |
|---|---|---|
| Years accuracy | years of experience within +/- 1 year | 90% |
| Roles found | labelled roles found; an invented employer counts as not found | 80% |
| Skills found | labelled skills found | 80% |
| Grounding | extracted skills, employers and institutions that actually appear in the CV | 95% |
| Language | CV language detected correctly | 100% |

Grounding needs no labels and catches the most harmful errors (invented employers, courses turned into skills).
Run it before go-live and after any change of model, prompt or CV Reading Mode, and keep the passed runs with the
evaluation runs as evidence. The demo site comes with the 5 demo applicants already labelled.

Cost: each CV now uses 2 model calls (parse, then score) plus 1 vision call per scanned or designed CV (up to 5 pages).

### LLM server requirements
- OpenAI-compatible `/v1/chat/completions` with **tool calling** (assistant) and **JSON schema / JSON mode**
  (scoring). With vLLM, enable tool calling (`--enable-auto-tool-choice --tool-call-parser <parser for your model>`).
  Guided JSON is built in.
- Context of 32k tokens or more is enough. CVs are truncated to *Max CV Characters* (default 40,000).
- Sizing guide for a 27B dense model: one 80 GB GPU (FP16/BF16), or one 48 GB GPU with FP8/AWQ quantisation.
  Expect about 5 to 15 seconds per CV.
- The app retries without `json_schema` / `reasoning` if the server rejects them, so most servers work.
- **Vision input** (images in chat messages) for reading scanned and designed CVs. Serve a vision-language model
  (Qwen3.8 27B accepts images), or set a separate *Vision Model* in settings. With vLLM, set a sensible
  `--limit-mm-per-prompt image=5`.

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
      Writes (status Open/Hold/Replied, interviews, new job openings, rubric changes, candidate emails) need an
      **Approve** click by the same user. Proposals are single-use and expire after 30 minutes. Reject, accept
      and delete are never exposed.
- [ ] **Audit:** every assistant turn (question, reply, tool calls, proposals, tokens, latency) and every
      approval is in **AI Chat Log** (append-only; HR Manager can read, System Manager can delete). Scoring and
      approvals are also comments on the record, field changes are in the Version log, errors in *Error Log*.
      Set *Keep Chat Logs (days)* to the retention policy.
- [ ] **Usage limits** set per user (messages per minute and day, tokens per day).
- [ ] **AI governance:** scores are recommendations. Keep a human decision step in your recruitment SOP, and
      document the rubric per job opening (the *AI Scoring Criteria* field).
- [ ] **Evaluation before go-live:** run an **AI Evaluation Run** (section 8) per job family, and again after
      any model, prompt or rubric change. Keep the passed runs as evidence for the architecture committee.
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

## 8. Evaluation and bias testing

Desk → **AI Evaluation Run** → New → pick a Job Opening with real or realistic applicants → Save. It runs in
the background (long queue). Nothing on the applicants is changed. For each sampled applicant it:

| Check | What changes | Pass when |
|---|---|---|
| Consistency | nothing (scored `Repeats` times) | spread within threshold |
| Fairness: different name | a different name is added | score change within threshold |
| Fairness: unlabelled demographics | a sentence about age, family or faith | score change within threshold |
| Fairness: labelled attributes | Nationality / Gender / Age / Religion lines (tests redaction) | score change within threshold |
| Injection | instructions addressed to the AI appended to the CV | score increase within threshold |

The result is a PASS/FAIL report with a per-applicant table. Each check runs the full production pipeline
(parse, then score), so the cost is about `2 x (repeats + 4) x sample size` model calls. If fairness or injection fails, change the model, the prompt or the rubric wording before relying on scores.

---

## 9. Testing

- **Unit tests** (no Frappe needed): `cd frappe/apps/ai_recruitment && python -m unittest discover -s tests`
  and `node --test tests/js/*.test.js`. They run in GitHub Actions on every push (`.github/workflows/ci.yml`),
  which also fails the build if an em dash appears in source.
- **Integration tests** (mocked LLM) on a test site:
  ```bash
  bench --site test_site set-config allow_tests true
  bench --site test_site run-tests --app ai_recruitment
  ```
  They cover weighted scoring, must-have gaps, redaction before the model, single-use approvals, refused
  statuses, chat logging and em dash removal.

---

## 10. Known limitations and next steps

- **OCR** reads up to 5 pages per scanned or designed CV. Handwritten or very low-quality scans may still fail and
  are marked *Failed* with a reason.
- Multi-column detection is a heuristic based on text start positions. Check *CV Read As* on a sample of applicants.
- `resume_link` (external URLs) is intentionally not fetched, to avoid server-side requests to arbitrary sites.
- The careers page is Frappe HR's standard `/jobs`. Arabic/RTL presentation, ministry branding and UAE PASS
  for candidates are separate work items.
- The demo's Railway setup runs everything in one container with no high availability. On-prem should run the multi-service
  compose or Helm deployment above.

---

- **Arabic UI** strings are in `ai_recruitment/locale/ar.po` (and `translations/ar.csv` for compatibility).
  Add new strings there when you add UI text.

---

## 11. House style

AI output must never contain em dashes. The rule is in both system prompts (scoring and assistant), and
`utils.no_em_dash` / `utils.clean` remove any that slip through from every model reply and streamed chunk
before it is saved or shown. Unit tests and CI enforce it. Keep this when changing models or prompts.
The repo's own text follows the same rule.
