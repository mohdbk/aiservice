# moet_careers

Public careers portal for the Ministry of Economy & Tourism, built on the UAE Design System (`@aegov/design-system`).

- Arabic by default with an English toggle, right-to-left layout for Arabic.
- Applicants sign in with email and password, apply with a CV, and follow their applications.
- Applications are Frappe HR Job Applicants; CVs are read and scored by `ai_recruitment`.

Pages live under `/careers`. Branding (logo, names, footer links) is set in **MoET Careers Settings**.

The stylesheet is compiled from `frontend/input.css` with Tailwind 4:

```
cd frontend && npm install && npm run build
```

See `docs/ON_PREM.md` in the repository for deployment.

Made by the Ministry of Economy & Tourism. License: MIT.
