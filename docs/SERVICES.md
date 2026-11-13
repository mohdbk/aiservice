# Recruitment System Services

Made by the Ministry of Economy & Tourism. Everything is in this repository, one folder per service:

| Service | Folder | Image | Public | Purpose |
|---|---|---|---|---|
| Recruitment API | `backend/` | `docker build backend` | No (private network) | .NET 10 Clean Architecture API on SQL Server: portal API, backoffice API, background jobs |
| Recruitment Backoffice | `backoffice/web/` | `docker build backoffice/web` | Yes (staff) | React app on nginx; forwards `/api` to the API on the same origin |
| Recruitment Agent | `agent/` | `docker build agent` | No | Microsoft Agent Framework assistant over AG-UI with A2UI; reached only through the API |
| Careers Portal | `portal/` | `docker build portal` | Yes (candidates) | Candidate site and its small server, which calls the portal API with a service key |
| SQL Server | (image) | `mcr.microsoft.com/mssql/server:2022-latest` | No | Database; schema scripts run by the API on start |

## How They Connect

```
Staff browser  ->  Backoffice (nginx)  --/api-->  API  --SQL-->  SQL Server
                                                   |  \
                                                   |   --AG-UI + staff session-->  Agent  --tools as the staff member-->  API
Candidate browser  ->  Portal (Node)  --service key-->  API /api/portal
```

## Settings by Service

- **Backoffice:**
  - `API_URL`: for example `http://recruitment-api:8080`.
  - `PORT`: defaults to 8080.
- **API:**
  - Database and portal: `DB_CONNECTION`, `PORTAL_API_KEY`, `PORTAL_API_SECRET`, `PORTAL_URL`.
  - First administrator: `ADMIN_EMAIL`, `ADMIN_PASSWORD` (at least 12 characters).
  - Assistant: `AGENT_URL`, `AGENT_KEY`.
  - Email: `SMTP_*`.
  - Model for CV scoring and drafts: `LLM_BASE_URL`, `LLM_MODEL`, `LLM_API_KEY`.
  - Other: `OCR_URL`, `TIME_ZONE`, `COOKIE_SECURE`, `TRUST_PROXY`, `RUN_WORKERS`.
  - Monitoring: `OTEL_EXPORTER_OTLP_ENDPOINT` (and optionally `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_SERVICE_NAME`) sends traces, metrics and logs over OpenTelemetry. The API and the agent support it; the API adds counters for emails, texts, background jobs and backups. The System Health page in the backoffice shows the same checks.
  - Backups: `BACKUP_S3_ENDPOINT`, `BACKUP_S3_BUCKET`, `BACKUP_S3_REGION`, `BACKUP_S3_ACCESS_KEY`, `BACKUP_S3_SECRET_KEY` (any S3-compatible store, such as MinIO on-prem; `BACKUP_S3_PATH_STYLE=1` for path-style), `BACKUP_INTERVAL_HOURS` (6), `BACKUP_KEEP` (28) and `BACKUP_RESTORE_ON_EMPTY` (on). On-prem, also keep SQL Server's own full and log backups.
  - Virus scanning: `CLAMAV_HOST` (`host` or `host:port`, 3310 by default) scans every upload (CVs, test answer files, assistant attachments) with ClamAV before it is stored; an infected file is refused. `CLAMAV_REQUIRED=1` also refuses uploads while the scanner cannot be reached (otherwise they are accepted and the failure is logged and alerted). On-prem, run the `clamav/clamav` container next to the API; System Health shows its state.
  - PDF letters: printed by Chromium through Microsoft Playwright (Apache 2.0, free), from the same HTML as the letter preview, so Arabic shapes and runs right to left as in the browser. The image installs Chromium (headless shell) into `PLAYWRIGHT_BROWSERS_PATH` (`/ms-playwright`) and the Noto fonts, so letters print offline; scripts are off and nothing is fetched while printing. Outside the image, run `pwsh playwright.ps1 install --with-deps --only-shell chromium` from the API's folder once.
- **Agent:** `BACKEND_URL`, `AGENT_KEY`, `LLM_*`. The agent's settings can also be changed in the backoffice under Setup › Assistant Settings.

## On-Prem

- Run each folder's image on the internal network.
- Publish only the backoffice (to staff) and the portal (to the internet, in the DMZ). The API, agent and SQL Server stay internal.
- Use a licensed SQL Server edition, on storage with 512-byte or 4 KB sectors, and back it up.

## Demo on Railway

- The demo runs the five services above in one Railway project.
- **SQL Server data does not persist:** Railway's volume storage gives SQL Server misaligned disk writes, and SQL Server crashes on start when its files are there. The demo database is therefore kept in the container, and a SQL Server redeploy starts it empty.
- **Refilling the demo:** the API recreates the schema, then running the **Demo Seed** job (`backoffice/scripts`) loads the demo data again.
- This affects the demo only. On-prem uses normal SQL Server storage.
