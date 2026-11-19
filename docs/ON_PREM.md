# On-Premises Deployment Guide

**Ministry of Economy & Tourism: Recruitment System (careers portal, backoffice, API, assistant)**

This guide covers moving the system from the Railway demo to ministry servers, on **Windows Server with IIS** (section 3)
or on **containers** (section 4). Nothing in the production setup depends on Railway or on any outside service: the
model, email, virus scanning and backups all point at ministry systems.

---

## 1. What you are deploying

| Part | Folder | What it is | Public |
|---|---|---|---|
| **Backoffice** | `backoffice/web` | The staff app (React). Its web server also forwards `/api` to the API, so staff browsers talk to one address | Staff only (internal network or VPN) |
| **Careers portal** | `portal` | The candidate site (React) and its small Node server, which is the only part that calls the API, with a service key | Internet (DMZ) |
| **API** | `backend` | .NET 10 API on SQL Server: the portal API, the backoffice API, background jobs (emails, scoring, reminders, backups), PDF letters | No |
| **Agent** | `agent` | .NET 10 assistant (Microsoft Agent Framework). Only the API calls it; its tools act as the signed-in staff member | No |
| **SQL Server** | | 2019 or later, licensed edition. The API creates and updates the schema itself on start | No |
| **Model server** | | Any OpenAI-compatible endpoint (vLLM recommended), in-country or on-prem | No |
| **ClamAV** | | Virus scanning of every upload (`CLAMAV_HOST`) | Recommended |
| **OCR service** | | Optional (`OCR_URL`): reads scanned and photographed CVs. Without it, PDF text and Word CVs are read and image-only CVs are marked unreadable for a person to check | Optional |

```
Staff browser ──HTTPS──► Backoffice site ──/api──► API ──► SQL Server
                                                    │ ├──► model server, SMTP relay, ClamAV, OCR, backup store
                                                    │ └──► Agent ──tools, as the staff member──► API
Candidate browser ──HTTPS──► Careers portal site (DMZ) ──service key──► API /api/portal
```

The browser never calls the API directly. The backoffice reaches it through its own address (`/api`), so the staff
sign-in cookie stays `SameSite=Strict` and the API's same-origin check holds. The portal's Node server calls it with
`Authorization: token <key>:<secret>`, so the API never faces the internet.

---

## 2. Choose a hosting model

| | Windows Server and IIS (section 3) | Containers (section 4) |
|---|---|---|
| Fits | Ministry Windows estate, IIS skills, Windows patching and monitoring | A container platform (Kubernetes, OpenShift, Docker hosts) |
| Package | One zip from the **IIS Package** workflow, installed by `Install-Moet.ps1` | One image per folder (`Dockerfile` in each) |
| Public sites | Backoffice and careers portal (two IIS sites) | The same two, behind the platform's ingress |

Both run the same code with the same settings (`docs/SERVICES.md`).

---

## 3. Windows Server and IIS

### 3.1 Layout

Four IIS sites. Two are public; the API and the agent are IIS sites too but bound to the machine or to the internal
network only.

| IIS site | App pool | Runs | Default binding |
|---|---|---|---|
| MoET Recruitment Backoffice | `MoetRecruitmentBackoffice` | Static React app; `/api` forwarded by URL Rewrite and Application Request Routing (ARR) | `https://recruitment.moet.gov.ae` |
| MoET Careers Portal | `MoetCareersPortal` | Node 22 server started and kept running by IIS through the ASP.NET Core Module (out of process) | `https://careers.moet.gov.ae` |
| MoET Recruitment API | `MoetRecruitmentApi` | .NET 10, in-process, always running (background jobs) | `http://127.0.0.1:5080` |
| MoET Recruitment Agent | `MoetRecruitmentAgent` | .NET 10, in-process | `http://127.0.0.1:5090` |

Usual placements:

- **Two servers (recommended):** an internal server with the API, agent and backoffice; a DMZ server with the careers
  portal only. The API site then also listens on the internal server's address (preferably HTTPS with an internal
  certificate), and the firewall allows only the DMZ server to reach it.
- **One server:** all four on one internal server, with only the portal's HTTPS published to the internet through the
  reverse proxy or WAF. Simpler, but the candidate site then shares a machine with the staff system.

### 3.2 Server prerequisites

| Item | Where it is needed | Notes |
|---|---|---|
| Windows Server 2019 or later | All | 2022 or 2025 recommended |
| IIS with **Application Initialization** and **IIS Management Scripting Tools** | All | `Install-WindowsFeature Web-Server, Web-AppInit, Web-Scripting-Tools, Web-Mgmt-Console` |
| **.NET 10 Hosting Bundle** | All (it provides the ASP.NET Core Module, which also runs the portal's Node server) | From [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0). Install it **after** IIS, then `net stop was /y` and `net start w3svc` |
| **URL Rewrite 2.1** | Backoffice server | From [iis.net](https://www.iis.net/downloads/microsoft/url-rewrite) |
| **Application Request Routing 3.0** | Backoffice server | From [iis.net](https://www.iis.net/downloads/microsoft/application-request-routing) |
| **Node.js 22 LTS** (x64) | Portal server | Installed to `C:\Program Files\nodejs`. The portal server has no npm packages, so nothing else is installed |
| TLS certificates | Public sites (and the API site when the portal is on another server) | In `Cert:\LocalMachine\My`; the script takes the thumbprint |

The servers need no internet access: the package carries the built apps and Chromium.

### 3.3 Get the package

Either:

- GitHub Actions, **IIS Package** workflow (runs on every change to the IIS files, or on demand): download the
  `moet-recruitment-iis` artifact. The same run installs the package on a fresh IIS and checks the four sites; see
  section 3.9.
- Or on a Windows build machine with the .NET 10 SDK, Node 22 and internet access:
  `powershell -ExecutionPolicy Bypass -File deploy\iis\Build-Package.ps1` (writes `out\moet-recruitment-iis.zip`).

The zip holds `api`, `agent`, `backoffice`, `portal`, `ms-playwright` (Chromium for PDF letters), `iis` (the install
script, templates and settings example) and `VERSION.txt` (commit and build time).

### 3.4 Settings

Unzip on each server (for example to `D:\Packages\moet-recruitment-iis`), copy `iis\moet.settings.example.psd1` to
`iis\moet.settings.psd1` and fill it in. It names the sites, bindings, certificates and each app's settings. The
script writes them into each site's `web.config` as environment variables and limits that folder to administrators,
SYSTEM and the site's app pool. Keep the settings file where only administrators can read it, or delete it after
installing and keep the values in the ministry's secrets store.

Must match between parts:

| API | Portal | Agent |
|---|---|---|
| `PORTAL_API_KEY`, `PORTAL_API_SECRET` | `CAREERS_PORTAL_API_KEY`, `CAREERS_PORTAL_API_SECRET` | |
| `AGENT_KEY`, `AGENT_URL` | | `AGENT_KEY`, `BACKEND_URL` (the API) |

`AGENT_KEY` also encrypts secrets stored in the database (such as model keys saved in Setup). Choose it once; changing
it later makes those stored secrets unreadable, and they have to be entered again.

Generate keys and secrets with, for example:
`[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])`

Section 5 lists every setting.

### 3.5 SQL Server

- Create the database (for example `MoetRecruitment`) and give the API's account **db_owner** on it: the API runs the
  schema scripts in `backend/src/Moet.Recruitment.Infrastructure/Persistence/Schema` on every start, in order, each
  once.
- **Windows sign-in (recommended):** run the API's app pool as a gMSA or service account (`Identity` in the settings
  file; for a gMSA, `MOET\svc-recruit$` with an empty password) and use `Integrated Security=True` in
  `DB_CONNECTION`. With the default app pool identity on the same machine, the login is
  `IIS AppPool\MoetRecruitmentApi`.
- **SQL sign-in:** `User Id=...;Password=...` in `DB_CONNECTION`.
- Use `Encrypt=True` and a trusted certificate on SQL Server (`TrustServerCertificate=True` only for testing).
- The first administrator comes from `ADMIN_EMAIL` and `ADMIN_PASSWORD` (12 characters or more) on the first start.
  Remove `ADMIN_PASSWORD` from the settings afterwards and run the script again.

### 3.6 Install

As an administrator, from the unzipped package:

```powershell
# Everything on one server
powershell -ExecutionPolicy Bypass -File iis\Install-Moet.ps1

# Two servers
powershell -ExecutionPolicy Bypass -File iis\Install-Moet.ps1 -Roles Api,Agent,Backoffice   # internal server
powershell -ExecutionPolicy Bypass -File iis\Install-Moet.ps1 -Roles Portal                 # DMZ server
```

The script:

1. Checks the prerequisites for the roles asked for, and that the paired settings match.
2. Copies each app to `InstallRoot` (`api`, `agent`, `backoffice`, `portal`, `ms-playwright`).
3. Writes each site's `web.config` from `iis\templates` with the settings, and checks it is valid XML.
4. Creates or updates the app pools: no managed code, always running, no idle timeout, no scheduled recycling, user
   profile loaded; the API's pool never runs two workers at once, so background jobs and schema updates never run
   twice during a recycle.
5. Creates or updates the sites, bindings and certificates; anonymous requests run as the app pool, Windows and Basic
   sign-in are off.
6. On the backoffice server: turns on ARR's proxy, keeps the original `Host` (the API's same-origin check needs it),
   turns off response buffering (the assistant streams its answers) and sets a 10 minute timeout.
7. Starts everything and checks each app answers: the API and agent `/healthz`, the backoffice and its `/api` proxy,
   the portal. A site on HTTPS only is not checked from the script; open it in a browser.

The API's first start creates the database tables and can take a few minutes; the script waits up to 5.

### 3.7 Update

Unzip the new package and run the same command with the same settings file. Each app shows a short "Updating" page
(IIS `app_offline.htm`) for the few seconds the copy takes. The database is updated by the API on start, so take a database backup before
updating; to go back, restore it and run the script from the previous package.

### 3.8 How each site is set up

- **Backoffice:** IIS serves the built files. Built assets (`/assets/*`) are cached for a year, `index.html` is checked
  on every visit, other paths fall back to `index.html` (the React router). `/api/*` is rewritten to `ApiUrl` with the
  undecoded address, so encoded characters reach the API unchanged. The security headers match the container
  version (CSP, `frame-ancestors 'none'`, HSTS); on `/api` only HSTS is added because the API sets its own.
- **Careers portal:** the ASP.NET Core Module starts `node.exe server\src\server.js`, gives it a private port, forwards
  every request to it and restarts it if it stops. The Node server listens on `127.0.0.1` only, serves the pages and
  its API, and takes the client address for its rate limits from the last `X-Forwarded-For` entry, the one IIS adds.
  If a load balancer or WAF in front also adds that header, set `CLIENT_IP_HEADER` to the header it sets for the
  client (for example `x-real-ip`) and `CLIENT_IP_FROM` to `first`.
- **API and agent:** ASP.NET Core in-process. `TRUST_PROXY=1` on the API takes the client address and scheme from the
  backoffice proxy. Compression is off so the assistant's stream is not held back.

### 3.9 What the IIS Package workflow checks

On a fresh Windows Server runner it installs IIS, the Hosting Bundle, URL Rewrite and ARR, installs the package with
`Install-Moet.ps1` under Windows PowerShell 5.1, and checks: the backoffice app, its routes, cached assets and
headers; the `/api` proxy, encoded characters and the same-origin check (a write from another site is refused, a
write from the backoffice passes); the portal's pages and assets through Node, that it never serves its own source,
that it reaches the API with its key and that its same-origin check passes; the API and agent health; and a PDF
letter printed with the bundled Chromium. There is no SQL Server on the runner, so the API runs without its database
there.

### 3.10 PDF letters

Offer and other letters are printed by Chromium (Playwright headless shell) from the same HTML as the preview. The
package's `ms-playwright` folder is copied to `InstallRoot\ms-playwright` and the API finds it through
`PLAYWRIGHT_BROWSERS_PATH`; nothing is downloaded and nothing is fetched while printing. For the same letter look as
the Linux image, install the **Noto Sans** and **Noto Kufi Arabic** fonts on the API server (Google Fonts, SIL Open
Font License); otherwise Windows fonts with Arabic support are used.

### 3.11 Logs and troubleshooting

- The API and agent log warnings and errors to the Windows **Event Log** (Application). For a start-up failure set
  `stdoutLogEnabled="true"` in that site's `web.config` for a while and read `logs\stdout*`.
- The portal's Node output is in `portal\logs\node*`.
- With `OTEL_EXPORTER_OTLP_ENDPOINT` set, the API and agent send traces, metrics and logs to the ministry's
  OpenTelemetry collector. **System Health** in the backoffice shows the database, email, scanner, model and backups.

| Symptom | Cause and fix |
|---|---|
| HTTP 500.19 on the backoffice | URL Rewrite or ARR is not installed |
| HTTP 500.30 or 502.5 on the API or agent | The app did not start: Event Log, then `stdoutLogEnabled`. Often `DB_CONNECTION` or SQL permissions |
| HTTP 502.5 on the portal | `node.exe` not found or not Node 22, or a portal setting missing (`SESSION_SECRET` under 32 characters, key or secret empty): `portal\logs\node*` |
| Staff sign-in works but every change says "forbidden" | ARR is not keeping the original Host: run the script again on the backoffice server, or set *Preserve client IP / host* in ARR's proxy settings |
| Staff sign-in does nothing on HTTP | The sign-in cookie needs HTTPS. For a test without TLS only, set `COOKIE_SECURE=0` on the API |
| Assistant answers appear all at once | Response buffering or compression: ARR `responseBufferLimit` must be 0, dynamic compression off on `/api` |
| HTTP 502.3 after about two minutes | ARR timeout: the script sets 10 minutes |
| HTTP 404.13 or 413 on uploads | Request size: 25 MB on the backoffice, 32 MB on the portal, 50 MB on the API |
| PDF letters fail | The API pool cannot read `ms-playwright`, or `PLAYWRIGHT_BROWSERS_PATH` is wrong |

---

## 4. Containers

Each folder has a `Dockerfile` (`backend`, `agent`, `backoffice/web`, `portal`). Run them on the internal network,
publish only the backoffice (to staff) and the portal (to the internet, in the DMZ), and give the API, agent and SQL
Server no public route. `docs/SERVICES.md` lists the images and how they connect. The API image already contains
Chromium and the Noto fonts.

---

## 5. Settings

Every setting, by part, is in `docs/SERVICES.md`. The ones needed to start:

| Part | Settings |
|---|---|
| API | `DB_CONNECTION`, `PORTAL_API_KEY`, `PORTAL_API_SECRET`, `PORTAL_URL`, `BACKOFFICE_URL`, `ADMIN_EMAIL`, `ADMIN_PASSWORD` (first start), `AGENT_URL`, `AGENT_KEY`, `LLM_BASE_URL`, `LLM_MODEL`, `LLM_API_KEY`, `SMTP_*`, `TIME_ZONE` |
| Agent | `BACKEND_URL`, `AGENT_KEY`, `LLM_BASE_URL`, `LLM_MODEL`, `LLM_API_KEY` |
| Backoffice | the API address (`ApiUrl` on IIS, `API_URL` in the container) |
| Careers portal | `RECRUITMENT_API_URL`, `CAREERS_PORTAL_API_KEY`, `CAREERS_PORTAL_API_SECRET`, `SESSION_SECRET` |

### Careers portal settings

| Setting | Default | Meaning |
|---|---|---|
| `RECRUITMENT_API_URL` | | The API, as the portal server reaches it |
| `CAREERS_PORTAL_API_KEY`, `CAREERS_PORTAL_API_SECRET` | | The API's `PORTAL_API_KEY` and `PORTAL_API_SECRET` |
| `SESSION_SECRET` | | 32 characters or more; signs the candidate's session cookie |
| `SESSION_HOURS` | 8 | Candidate session length |
| `COOKIE_SECURE` | 1 | Session cookie only over HTTPS (and HSTS sent) |
| `PORT` / `ASPNETCORE_PORT` | 8080 | Port to listen on; IIS sets `ASPNETCORE_PORT` |
| `HOST` | all addresses | Address to listen on; `127.0.0.1` under IIS |
| `TRUST_PROXY` | 0 | Take the client address from a proxy header |
| `CLIENT_IP_HEADER` | `x-forwarded-for` | That header |
| `CLIENT_IP_FROM` | `first` | `last` when the proxy in front appends the address it saw (IIS does) |
| `MAX_BODY_MB` | 30 | Largest request (CVs are sent as JSON) |
| `FRAPPE_TIMEOUT_MS` | 30000 | Time allowed for an API call |

---

## 6. The model

- Any OpenAI-compatible `/v1/chat/completions` endpoint with **tool calling** (the assistant) and **JSON schema or JSON
  mode** (CV scoring and drafts). With vLLM, turn tool calling on (`--enable-auto-tool-choice --tool-call-parser
  <parser for your model>`).
- Set `LLM_BASE_URL`, `LLM_MODEL` and `LLM_API_KEY` on the API and the agent. The assistant's settings can also be
  changed in the backoffice under **Setup > Assistant Settings**. `LLM_SCORE_MODEL` scores CVs with a different
  (stronger) model; `CV_SCORE_TWICE=1` scores each CV twice and asks for a person when the two disagree.
- A context of 32k tokens or more is enough. Sizing guide for a 27B dense model: one 80 GB GPU (FP16/BF16), or one
  48 GB GPU with FP8 or AWQ quantisation; expect about 5 to 15 seconds per CV.
- CVs are redacted of name, contacts, nationality, age, gender and religion before they are sent, but they are still
  personal data: keep the model in-country or on-prem.
- Token use is recorded for every call (**Assistant Usage**); `LLM_PRICE_INPUT` and `LLM_PRICE_OUTPUT` add a cost
  estimate.

---

## 7. Network and security checklist

- [ ] **Inbound from the internet:** HTTPS to the careers portal only, through the reverse proxy or WAF.
- [ ] **Staff:** HTTPS to the backoffice from the ministry network or VPN only.
- [ ] **DMZ to internal:** the portal server to the API only (port 5080, or the API site's HTTPS port). Nothing else.
- [ ] **API outbound:** SQL Server (1433), the model server, the SMTP relay, ClamAV (3310), the OCR service and the
      backup store, if used. **Agent outbound:** the API and the model server.
- [ ] TLS certificates on the two public sites; HSTS is sent by both.
- [ ] Settings files and `web.config` readable by administrators and the site's own app pool only (the install
      script sets this). No secrets in source control.
- [ ] First administrator's password changed after the first sign-in, and `ADMIN_PASSWORD` removed from the settings.
- [ ] **Assistant permissions:** its tools act as the signed-in staff member with that person's role. Every change it
      proposes needs an **Approve** click by the same person; admin tools are hidden by role and checked again by
      the API.
- [ ] **Audit:** staff actions and assistant turns are in the audit log in the backoffice.
- [ ] **AI governance:** scores are recommendations; people make every decision. Each job's requirements (Essential
      and Desirable) are approved by HR before scoring. Staff can correct a requirement's score with a reason; the
      correction is kept and **Hiring Report** shows how often staff and scores agree.
- [ ] ClamAV signatures updated from an internal mirror; `CLAMAV_REQUIRED=1` once the scanner is dependable.

---

## 8. Backups and monitoring

- **SQL Server:** the ministry's usual full and log backups of the database, with a restore tested quarterly. Files
  (CVs, letters, attachments) are stored in the database, so this covers them.
- **Built-in backups:** the API can also export the data to any S3-compatible store (MinIO on-prem) every
  `BACKUP_INTERVAL_HOURS` and keep `BACKUP_KEEP` copies (`BACKUP_S3_*` settings). By default an empty database is
  refilled from the newest export when the API starts (`BACKUP_RESTORE_ON_EMPTY`), which suits the demo; in
  production set `BACKUP_RESTORE_ON_EMPTY=0` and restore deliberately, and use a bucket of its own.
- **Monitoring:** System Health in the backoffice, the Windows Event Log or container logs, OpenTelemetry where set up,
  and the model server's latency.

---

## 9. Before going live

1. **Legal review.** UAE PDPL (Federal Decree-Law 45 of 2021) gives people the right to information about how their
   data is processed and to object to decisions made by automated processing alone. Hiring decisions here are
   always made by people. The portal shows no privacy notice, so ask the legal and data protection team to confirm
   this, or add a notice page in `portal/web`.
2. **Email.** Point `SMTP_*` at the ministry relay and set `PORTAL_URL` and `BACKOFFICE_URL`, so activation, password
   reset, interview and staff emails go out with the right links.
3. **Fonts.** Both apps bundle their fonts; no font is fetched from the internet.
4. **UAE PASS** is not wired in yet. It would be added to the portal server as an OpenID Connect sign-in; email
   accounts stay for applicants abroad.
5. **Evaluation.** Score a sample of real, anonymised CVs per job family and compare with HR's own review before
   relying on the scores; the live CV evaluations (`backend/tests/.../CvLiveEvals.cs`) include a fairness check.

---

## 10. Migrating from the Railway demo

The demo holds fictional data only. Start the on-prem system with an empty database: the API creates the schema and
the first administrator, and staff create jobs and settings in the backoffice. Do not copy demo applicants, and do not
point `BACKUP_S3_*` at the demo's bucket: an empty database would be refilled from it.

---

## 11. House style

- No em dashes anywhere: not in the apps' text, the model's answers (removed before they are saved or shown) or this
  repository. CI checks it.
- English headings, page names, field labels, navigation, buttons and status labels use **Title Case** ("Create Your
  Account", "Country of Residence"). Body text, hints and notices use sentence case.
- Arabic is the default language. Email, phone and password fields stay left to right in Arabic pages.
- Candidate-facing text never mentions AI, a privacy notice or who reviews applications.
- Icons are **Phosphor** (`@phosphor-icons/react`, regular weight), the UAE Design System icon set. Arrows and carets
  are mirrored in right-to-left pages.
- Components come from the UAE Design System. The ministry logo always sits on the left of the header and footer.
- The maker is the Ministry of Economy & Tourism.
