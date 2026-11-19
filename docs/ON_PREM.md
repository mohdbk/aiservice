# On-Premises Deployment Guide

**Ministry of Economy & Tourism: Recruitment System (careers portal, backoffice, API, assistant)**

This guide covers moving the system from the Railway demo to ministry servers, on **Windows Server with IIS** (section 3)
or on **containers** (section 4). Nothing in the production setup depends on Railway or on any outside service: the
model, email, virus scanning and backups all point at ministry systems.

---

## 1. What you are deploying

Two apps, each one ASP.NET Core (.NET 10) site, plus SQL Server and the ministry's own services:

| App | Folders | What is in it | Who reaches it |
|---|---|---|---|
| **Backoffice** | `backend` (with the assistant in `backend/src/Moet.Recruitment.Agent`), `backoffice/web` | The staff app (React), the API on SQL Server (portal API, backoffice API, background jobs for emails, scoring, reminders and backups, PDF letters) and the recruitment assistant (Microsoft Agent Framework), in one app | Staff only (internal network or VPN) |
| **Careers portal** | `portal/host`, `portal/web` | The candidate site (React) and its small server, the only part that calls the API, with a service key | Internet (DMZ) |
| **SQL Server** | | 2019 or later, licensed edition. The backoffice creates and updates the schema itself on start | No |
| **Model server** | | Any OpenAI-compatible endpoint (vLLM recommended), in-country or on-prem | No |
| **ClamAV** | | Virus scanning of every upload (`CLAMAV_HOST`) | Recommended |
| **OCR service** | | Optional (`OCR_URL`): reads scanned and photographed CVs. Without it, PDF text and Word CVs are read and image-only CVs are marked unreadable for a person to check | Optional |

```
Staff browser ──HTTPS──► Backoffice site (staff app + API + assistant) ──► SQL Server
                                     │                                └──► model server, SMTP relay, ClamAV, OCR, backup store
Candidate browser ──HTTPS──► Careers portal site (DMZ) ──service key──► Backoffice site /api/portal
```

The staff app and its API share one address, so the staff sign-in cookie stays `SameSite=Strict` and the API's
same-origin check holds. The assistant runs inside the backoffice app: it listens on the machine's loopback address
only, and its tools call the API in memory as the signed-in staff member. The portal's server calls the API with
`Authorization: token <key>:<secret>`; candidates' browsers never reach the API.

---

## 2. Choose a hosting model

| | Windows Server and IIS (section 3) | Containers (section 4) |
|---|---|---|
| Fits | Ministry Windows estate, IIS skills, Windows patching and monitoring | A container platform (Kubernetes, OpenShift, Docker hosts) |
| Package | One zip from the **IIS Package** workflow, installed by `Install-Moet.ps1` | One image per folder (`Dockerfile` in each) |
| Sites | Two IIS sites: backoffice and careers portal | The API, the backoffice web server and the portal, behind the platform's ingress |

Both run the same code with the same settings (`docs/SERVICES.md`).

---

## 3. Windows Server and IIS

### 3.1 Layout

Two IIS sites, both plain ASP.NET Core apps hosted in-process. IIS needs only the .NET 10 Hosting Bundle: no URL
Rewrite, no Application Request Routing, no Node.js.

| IIS site | App pool | What is in it | Default bindings |
|---|---|---|---|
| MoET Recruitment Backoffice | `MoetRecruitmentBackoffice` | Staff app, API and assistant; always running (background jobs) | `https://recruitment.moet.gov.ae`, plus `http://127.0.0.1:5080` for the portal on the same server |
| MoET Careers Portal | `MoetCareersPortal` | Candidate site and its server | `https://careers.moet.gov.ae` |

Usual placements:

- **Two servers (recommended):** the backoffice on an internal server, the portal on a DMZ server. The portal's
  `RECRUITMENT_API_URL` is then the backoffice's internal HTTPS address, and the firewall allows only the DMZ server to
  reach it.
- **One server:** both sites on one internal server, with only the portal's HTTPS published to the internet through
  the reverse proxy or WAF. The portal reaches the API on `http://127.0.0.1:5080`.

### 3.2 Server prerequisites

| Item | Notes |
|---|---|
| Windows Server 2019 or later | 2022 or 2025 recommended |
| IIS with **Application Initialization** | `Install-WindowsFeature Web-Server, Web-AppInit, Web-Scripting-Tools, Web-Mgmt-Console` |
| **.NET 10 Hosting Bundle** | From [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0). Install it **after** IIS, then `net stop was /y` and `net start w3svc` |
| TLS certificates | In `Cert:\LocalMachine\My`; the script takes the thumbprint |

The servers need no internet access: the package carries the built apps and Chromium for PDF letters.

### 3.3 Get the package

Download the **built** package, not GitHub's "Code > Download ZIP" (that is the source code):

- GitHub Actions, **IIS Package** workflow (runs on every change to the IIS files, or on demand), latest green run,
  artifact `moet-recruitment-iis`. The download is a zip holding `moet-recruitment-iis.zip`: unzip both. The same run
  installs the package on a fresh IIS and checks both sites; see section 3.9.
- Or on a Windows build machine with the .NET 10 SDK, Node 22 and internet access:
  `powershell -ExecutionPolicy Bypass -File deploy\iis\Build-Package.ps1` (writes `out\moet-recruitment-iis.zip`).

The package holds `backoffice` (the app with the staff app in `wwwroot`), `portal` (the app with the candidate site in
`wwwroot`), `ms-playwright` (Chromium), `iis` (the install script and settings example) and `VERSION.txt`.

### 3.4 Settings

Unzip on each server (for example to `C:\MoET\package`). In Windows PowerShell, run as administrator:

```powershell
cd C:\MoET\package
copy iis\moet.settings.example.psd1 iis\moet.settings.psd1
notepad iis\moet.settings.psd1
```

The file names the sites, bindings and certificates, and holds each app's settings. `InstallRoot` must be a separate
folder from the package (for example `D:\MoET\Recruitment`). The script writes the settings into each site's
`web.config` as environment variables and limits that folder to administrators, SYSTEM and the site's app pool. Keep
the settings file where only administrators can read it, or delete it after installing and keep the values in the
ministry's secrets store.

Must match between the two sites: the backoffice's `PORTAL_API_KEY` and `PORTAL_API_SECRET` are the portal's
`CAREERS_PORTAL_API_KEY` and `CAREERS_PORTAL_API_SECRET`.

`AGENT_KEY` (16 characters or more) protects the assistant inside the backoffice and encrypts secrets saved in the
database (such as model keys saved in Setup). Choose it once; changing it later makes those saved secrets unreadable,
and they have to be entered again.

Generate keys and secrets with, for example:
`[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])`

For a first test without certificates: `HttpsPort = 0` and `HttpPort = 8081` (backoffice) or `8082` (portal); the script
then also lets staff sign in over HTTP.

### 3.5 SQL Server

- Create the database (for example `MoetRecruitment`) and give the backoffice's account **db_owner** on it: the app runs
  the schema scripts in `backend/src/Moet.Recruitment.Infrastructure/Persistence/Schema` on every start, in order,
  each once.
- **Windows sign-in (recommended):** run the backoffice's app pool as a gMSA or service account (`Identity` in the
  settings file; for a gMSA, `MOET\svc-recruit$` with an empty password) and use `Integrated Security=True` in
  `DB_CONNECTION`. With the default app pool identity on the same machine, the login is
  `IIS AppPool\MoetRecruitmentBackoffice`.
- **SQL sign-in:** `User Id=...;Password=...` in `DB_CONNECTION`.
- Use `Encrypt=True` and a trusted certificate on SQL Server (`TrustServerCertificate=True` only for testing).
- The first administrator comes from `ADMIN_EMAIL` and `ADMIN_PASSWORD` (12 characters or more) on the first start.
  Remove `ADMIN_PASSWORD` from the settings afterwards and run the script again.

### 3.6 Install

From the package folder, in the same administrator window, one line at a time:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\iis\Install-Moet.ps1                    # both sites on this server
.\iis\Install-Moet.ps1 -Roles Backoffice  # internal server only
.\iis\Install-Moet.ps1 -Roles Portal      # DMZ server only
```

The script:

1. Checks the Hosting Bundle, the package and the settings, and that the paired keys match.
2. Copies each app to `InstallRoot` (`backoffice`, `portal`, `ms-playwright`).
3. Adds the settings to each app's `web.config` as environment variables.
4. Creates or updates the app pools: no managed code, always running, no idle timeout, no scheduled recycling, one
   worker at a time (background jobs and schema updates never run twice during a recycle), user profile loaded.
5. Creates or updates the sites, bindings and certificates; anonymous requests run as the app pool, Windows and Basic
   sign-in are off.
6. Starts both and checks they answer on their HTTP or local bindings: the API, the staff app, the sign-in check and
   the portal reaching the API. A site on HTTPS only is checked in a browser.

The backoffice's first start creates the database tables and can take a few minutes; the script waits up to 5.

### 3.7 Update

Unzip the new package, copy your `iis\moet.settings.psd1` into it, and run the same lines. Each site shows a short
"Updating" page (IIS `app_offline.htm`) while its files are replaced. The database is updated on start, so take a
database backup before updating; to go back, restore it and run the script from the previous package.

### 3.8 How each site is set up

- **Backoffice:** IIS runs the app in-process. Built assets (`/assets/*`) are cached for a year, every other page path
  returns the staff app (the React router), and `/api` is the API with its JSON errors. The security headers match the
  container version (CSP, `frame-ancestors 'none'`, HSTS on HTTPS). Compression is off for dynamic responses so the
  assistant's streamed answers are not held back. Uploads up to 50 MB.
- **Assistant:** started by the backoffice app on `127.0.0.1` with a free port; nothing else can call it (it needs
  `AGENT_KEY` and a staff session). Its tools call the API in memory, through the same sign-in, same-origin and audit
  checks as the browser.
- **Careers portal:** IIS runs the portal server in-process; it serves the candidate pages and its small API and calls
  the backoffice's `/api/portal` with the service key. IIS gives it the real client address for its rate limits. If a
  load balancer or WAF sits in front, set `TRUST_PROXY=1` and `CLIENT_IP_HEADER` to the header it sets (for example
  `x-real-ip`), or `CLIENT_IP_FROM=last` when it appends to `X-Forwarded-For`.

### 3.9 What the IIS Package workflow checks

On a fresh Windows Server runner it installs IIS and the Hosting Bundle only, installs the package with
`Install-Moet.ps1` under Windows PowerShell 5.1, and checks: the staff app, its routes, cached assets and headers; the
API, encoded characters and the same-origin check (a write from another site is refused, a write from the backoffice
passes); that neither site serves its `web.config`; the portal's pages and assets, that it reaches the API with its key
and that its same-origin check passes; and a PDF letter printed with the bundled Chromium. There is no SQL Server on
the runner, so the API runs without its database there; the full journey with a database (including the assistant)
runs in the End to End workflow, against the same two apps.

### 3.10 PDF letters

Offer and other letters are printed by Chromium (Playwright headless shell) from the same HTML as the preview. The
package's `ms-playwright` folder is copied to `InstallRoot\ms-playwright` and the backoffice finds it through
`PLAYWRIGHT_BROWSERS_PATH`; nothing is downloaded and nothing is fetched while printing. For the same letter look as
the Linux image, install the **Noto Sans** and **Noto Kufi Arabic** fonts on the server (Google Fonts, SIL Open Font
License); otherwise Windows fonts with Arabic support are used.

### 3.11 Logs and troubleshooting

- Both apps log warnings and errors to the Windows **Event Log** (Application). For a start-up failure set
  `stdoutLogEnabled="true"` in that site's `web.config` for a while and read `logs\stdout*`.
- With `OTEL_EXPORTER_OTLP_ENDPOINT` set, the backoffice sends traces, metrics and logs to the ministry's OpenTelemetry
  collector. **System Health** in the backoffice shows the database, email, scanner, model, assistant and backups.

| Symptom | Cause and fix |
|---|---|
| "powershell" or "robocopy is not recognized" | The server's PATH lacks `C:\Windows\System32`. Open `C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe` directly; the script itself calls Windows' tools by full path |
| "Settings file not found" | Copy `iis\moet.settings.example.psd1` to `iis\moet.settings.psd1` and fill it in |
| "The package has no 'backoffice' app" | That is the source code zip; download the built package (section 3.3) |
| HTTP 500.19 | The .NET 10 Hosting Bundle is missing, or was installed before IIS: install it again |
| HTTP 500.30 or 502.5 | The app did not start: Event Log, then `stdoutLogEnabled`. Often `DB_CONNECTION`, SQL permissions, or a portal setting missing (`SESSION_SECRET` under 32 characters, key or secret empty) |
| Staff sign-in does nothing on HTTP | The sign-in cookie needs HTTPS. The script allows HTTP only when the site has no HTTPS binding |
| The portal says the service is unavailable | It cannot reach `RECRUITMENT_API_URL`, or its key and secret differ from the backoffice's |
| HTTP 404.13 or 413 on uploads | Request size: 50 MB on the backoffice, 32 MB on the portal |
| PDF letters fail | The app pool cannot read `ms-playwright`, or `PLAYWRIGHT_BROWSERS_PATH` is wrong |

---

## 4. Containers

Each folder has a `Dockerfile` (`backend`, `backoffice/web`, `portal`). Run them on the internal network, publish only
the backoffice web server (to staff) and the portal (to the internet, in the DMZ), and give the API and SQL Server no
public route. The assistant runs inside the API container. `docs/SERVICES.md` lists the images and how they connect.
The API image already contains Chromium and the Noto fonts.

---

## 5. Settings

Every setting, by part, is in `docs/SERVICES.md`. The ones needed to start:

| Part | Settings |
|---|---|
| Backoffice (API and assistant) | `DB_CONNECTION`, `PORTAL_API_KEY`, `PORTAL_API_SECRET`, `PORTAL_URL`, `BACKOFFICE_URL`, `ADMIN_EMAIL`, `ADMIN_PASSWORD` (first start), `AGENT_KEY`, `LLM_BASE_URL`, `LLM_MODEL`, `LLM_API_KEY`, `SMTP_*`, `TIME_ZONE` |
| Backoffice web server (containers only) | `API_URL` |
| Careers portal | `RECRUITMENT_API_URL`, `CAREERS_PORTAL_API_KEY`, `CAREERS_PORTAL_API_SECRET`, `SESSION_SECRET` |

### Careers portal settings

| Setting | Default | Meaning |
|---|---|---|
| `RECRUITMENT_API_URL` | | The API, as the portal server reaches it |
| `CAREERS_PORTAL_API_KEY`, `CAREERS_PORTAL_API_SECRET` | | The API's `PORTAL_API_KEY` and `PORTAL_API_SECRET` |
| `SESSION_SECRET` | | 32 characters or more; signs the candidate's session cookie |
| `SESSION_HOURS` | 8 | Candidate session length |
| `COOKIE_SECURE` | 1 | Session cookie only over HTTPS (and HSTS sent) |
| `PORT` | 8080 | Port to listen on in a container (IIS chooses its own) |
| `TRUST_PROXY` | 0 | Take the client address from a proxy header (a load balancer or WAF in front) |
| `CLIENT_IP_HEADER` | `x-forwarded-for` | That header |
| `CLIENT_IP_FROM` | `first` | `last` when the proxy in front appends the address it saw |
| `MAX_BODY_MB` | 30 | Largest request (CVs are sent as JSON) |
| `API_TIMEOUT_MS` | 30000 | Time allowed for an API call |
| `WEB_DIST` | `wwwroot` | The built candidate app |

---

## 6. The model

- Any OpenAI-compatible `/v1/chat/completions` endpoint with **tool calling** (the assistant) and **JSON schema or JSON
  mode** (CV scoring and drafts). With vLLM, turn tool calling on (`--enable-auto-tool-choice --tool-call-parser
  <parser for your model>`).
- Set `LLM_BASE_URL`, `LLM_MODEL` and `LLM_API_KEY` on the backoffice. The assistant uses the same model unless it has its
  own (`AGENT_LLM_BASE_URL`, `AGENT_LLM_MODEL`, `AGENT_LLM_API_KEY`), and its settings can also be changed in the
  backoffice under **Setup > Assistant Settings**. `LLM_SCORE_MODEL` scores CVs with a different
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
- [ ] **DMZ to internal:** the portal server to the backoffice site's HTTPS port only. Nothing else.
- [ ] **Backoffice outbound:** SQL Server (1433), the model server, the SMTP relay, ClamAV (3310), the OCR service and
      the backup store, if used.
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
