# HRMS and Careers Portal Services

Made by the Ministry of Economy & Tourism. Everything is in this repository. Two apps, the same on IIS, in containers and
on the Railway demo:

| Service | Folder | Image | Public | Purpose |
|---|---|---|---|---|
| HRMS | `backend/`, `hrms/web/` | `docker build -f deploy/hrms.Dockerfile .` | Yes (staff) | The staff app (React), the .NET 10 API on SQL Server (portal API, staff API, background jobs, PDF letters) and the HRMS assistant (Microsoft Agent Framework, AG-UI with A2UI), in one app |
| Careers Portal | `portal/` | `docker build portal` | Yes (candidates) | Candidate site and its small .NET server (`portal/host`), which calls the portal API with a service key |
| SQL Server | (image) | `mcr.microsoft.com/mssql/server:2022-latest` | No | Database; schema scripts run by HRMS on start |

## How They Connect

```
Staff browser  ->  HRMS (staff app + API + assistant)  --SQL-->  SQL Server
                    (the assistant's tools call the API in memory as the staff member)
Candidate browser  ->  Portal (.NET)  --service key-->  HRMS /api/portal
```

## Settings by Service

- **HRMS (API and assistant):**
  - Database and portal: `DB_CONNECTION`, `PORTAL_API_KEY`, `PORTAL_API_SECRET`, `PORTAL_URL`.
  - First administrator: `ADMIN_EMAIL`, `ADMIN_PASSWORD` (at least 12 characters).
  - Assistant (inside the API): `AGENT_KEY` (16 characters or more; also encrypts saved secrets). It uses the API's model unless `AGENT_LLM_BASE_URL`, `AGENT_LLM_MODEL` and `AGENT_LLM_API_KEY` are set; its settings can also be changed under Setup › Assistant Settings.
  - Email: `SMTP_*`.
  - Portal API limits (a DMZ portal): `PORTAL_ONLY_PORTS` (ports where only `/api/portal` and `/healthz` answer) and `PORTAL_API_ALLOWED_IPS` (addresses or ranges allowed to call it).
  - Filling in from the CV (with the model): on sign-up a candidate can start with their CV; its name, email and phone fill in the form and it becomes their default CV (a CV from an unfinished sign-up is deleted after a day; five a hour per address). On applying, suggested answers to the job's screening questions come from the chosen CV for the candidate to check; questions about nationality, age, gender, marital status, religion, health, salary, availability and declarations are never suggested. Reading for these keeps the CV's personal details (it only fills in the candidate's own forms); scoring still reads the CV with them removed.
  - Scanned and photographed CVs: `OCR_URL` (an OCR service); without it the AI model reads the images when it accepts them (`LLM_VISION_MODEL`, or `LLM_MODEL`; `CV_READ_WITH_MODEL=0` turns it off). The image goes to the model as it is, so keep the model on-prem or in-country.
  - Model for CV scoring and drafts: `LLM_BASE_URL`, `LLM_MODEL`, `LLM_API_KEY`.
  - Longest model answer: `LLM_MAX_TOKENS` (default 8000) for the API and `AGENT_MAX_OUTPUT_TOKENS` (default 4000) for the assistant. Hosted services such as OpenRouter otherwise reserve credit for the model's whole output limit on every call.
  - Staff emails (morning digest, weekly summary): `HRMS_URL` (for example https://hrms.example.gov.ae) so the emails link to HRMS; each person switches them on from the notifications bell.
  - Cost estimate on Assistant Usage: `LLM_PRICE_INPUT` and `LLM_PRICE_OUTPUT` (price per million tokens) and `LLM_PRICE_CURRENCY` (default USD) on the API. Token counts are recorded for every model call, from the API features and from the assistant.
  - CV scoring: `LLM_SCORE_MODEL` scores CVs with a different (stronger) model than the rest; `CV_SCORE_TWICE=1` scores each CV twice and asks for a person to look when the two disagree by more than 2 points on a requirement (about twice the scoring cost).
  - Live CV evaluations (20 test CVs and the fairness check) run in the Agent Evals workflow when the `AGENT_EVAL_API_KEY` secret is set (or `CV_EVAL_API_KEY` locally), on changes to the scoring prompt or rules and weekly.
  - Other: `OCR_URL`, `TIME_ZONE`, `COOKIE_SECURE`, `TRUST_PROXY`, `RUN_WORKERS`.
  - Monitoring: `OTEL_EXPORTER_OTLP_ENDPOINT` (and optionally `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_SERVICE_NAME`) sends traces, metrics and logs over OpenTelemetry. The API (with the assistant) supports it; the API adds counters for emails, texts, background jobs and backups. The System Health page in HRMS shows the same checks.
  - Backups: `BACKUP_S3_ENDPOINT`, `BACKUP_S3_BUCKET`, `BACKUP_S3_REGION`, `BACKUP_S3_ACCESS_KEY`, `BACKUP_S3_SECRET_KEY` (any S3-compatible store, such as MinIO on-prem; `BACKUP_S3_PATH_STYLE=1` for path-style), `BACKUP_INTERVAL_HOURS` (6), `BACKUP_KEEP` (28) and `BACKUP_RESTORE_ON_EMPTY` (on). On-prem, also keep SQL Server's own full and log backups.
  - Virus scanning: `CLAMAV_HOST` (`host` or `host:port`, 3310 by default) scans every upload (CVs, test answer files, assistant attachments) with ClamAV before it is stored; an infected file is refused. `CLAMAV_REQUIRED=1` also refuses uploads while the scanner cannot be reached (otherwise they are accepted and the failure is logged and alerted). On-prem, run the `clamav/clamav` container next to the API; System Health shows its state.
  - PDF letters: printed by Chromium through Microsoft Playwright (Apache 2.0, free), from the same HTML as the letter preview, so Arabic shapes and runs right to left as in the browser. The image installs Chromium (headless shell) into `PLAYWRIGHT_BROWSERS_PATH` (`/ms-playwright`) and the Noto fonts, so letters print offline; scripts are off and nothing is fetched while printing. Outside the image, run `pwsh playwright.ps1 install --with-deps --only-shell chromium` from the API's folder once.

## On-Prem

- Windows Server and IIS: `deploy/iis` and `docs/ON_PREM.md` section 3 (two IIS sites: HRMS, with the staff app, API and assistant in one app, and the careers portal).
- Containers: the two images above. Publish HRMS to staff only and the portal to the internet (in the DMZ); SQL Server stays internal.
- Use a licensed SQL Server edition, on storage with 512-byte or 4 KB sectors, and back it up.

## Demo on Railway

- The demo runs the two apps and SQL Server in one Railway project, plus the Demo Seed job. Pushes to `main` deploy them.
- **SQL Server data does not persist:** Railway's volume storage gives SQL Server misaligned disk writes, and SQL Server crashes on start when its files are there. The demo database is therefore kept in the container, and a SQL Server redeploy starts it empty.
- **Refilling the demo:** HRMS recreates the schema, then running the **Demo Seed** job (`hrms/scripts`) loads the demo data again.
- This affects the demo only. On-prem uses normal SQL Server storage.
