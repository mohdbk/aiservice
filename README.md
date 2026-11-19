# MoET HRMS and Careers Portal

**Ministry of Economy & Tourism.** A careers portal for candidates and an HRMS for staff, with AI CV
screening against structured requirements, assessments (including the Big Five questionnaire), interviews, offers and
a HRMS assistant. Arabic first, on the UAE Design System.

## Repository layout

| Path | What |
|---|---|
| `backend/` | **HRMS app**: the .NET 10 API on SQL Server (portal API, staff API, background jobs, PDF letters) with the HRMS assistant inside it (`src/Moet.Hrms.Agent`) |
| `hrms/web/` | The staff app (React), served by HRMS app from `wwwroot` |
| `portal/web/` | The candidate site (React) |
| `portal/host/` | The candidate site's small .NET server: serves the site and calls the API with a service key |
| `portal/server/` | Design preview only: the earlier Node server with mock data, used for screenshots and local design work. Not deployed |
| `deploy/iis/` | Windows Server and IIS: build and install scripts for the two sites |
| `e2e/` | Browser tests, design audit and the walkthrough film |
| `docs/ON_PREM.md` | **On-premises deployment guide** (IIS and containers, settings, security) |
| `docs/SERVICES.md` | Services and every setting |
| `frappe/` | The earlier Frappe HR version, kept for reference |

## How it works

```
Staff browser ──► HRMS site (staff app + API + assistant) ──► SQL Server, model server, email, virus scanner
Candidate browser ──► Careers portal site (DMZ) ──service key──► HRMS site /api/portal
```

- The model is any OpenAI-compatible endpoint (vLLM on-prem). CVs are redacted before they are sent.
- Scores are recommendations: HR approves each job's requirements and people make every decision.
- The assistant can only propose changes; each one needs an Approve click by the same person.

## Deploying

- **IIS (two sites):** `deploy/iis/README.md`, then `docs/ON_PREM.md` section 3.
- **Containers:** `deploy/hrms.Dockerfile` (repository root as context) and `portal/Dockerfile`; `docs/SERVICES.md`.
- **Demo:** Railway deploys `main`: the same two apps, SQL Server and the Demo Seed job.
