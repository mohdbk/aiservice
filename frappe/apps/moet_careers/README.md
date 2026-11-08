# moet_careers

Frappe side of the Ministry of Economy & Tourism careers portal. The portal itself (React app and its
backend) lives in `portal/` and is hosted outside the ministry network; it calls this app's API.

- `portal_api.py`: the API for the portal backend. Only the **Careers Portal Service** role can call it
  (the service user `careers-portal@service.invalid`, authenticated with an API key). Errors are codes the
  portal turns into text in the visitor's language.
- `i18n.py`: portal languages, wording and job translations. The language model can suggest drafts; only
  rows HR approves are shown.
- **MoET Careers Settings**: logo, ministry website, languages, sign-up, CV size.
- **Portal Text**: HR-editable wording per language (English and Arabic ship in `portal_text/`).
- **Arabic first, English beside it**: jobs, screening questions and tests are written in Arabic with English
  fields next to them and **Suggest English** (`bilingual.py` stores the English as translation rows, so more
  languages can be added later).
- **Recruiter** and **Recruitment Admin** roles with a recruitment-only **Careers** workspace (`desk_setup.py`).

See `docs/ON_PREM.md`, section 4b.

Made by the Ministry of Economy & Tourism. License: MIT.
