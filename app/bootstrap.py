"""Idempotent setup of Frappe: custom fields, service user, webhook, optional demo data."""
import asyncio
import logging

from .config import settings
from .frappe import Frappe, FrappeError
from .state import state

log = logging.getLogger("bootstrap")

JA = "Job Applicant"
JO = "Job Opening"

CUSTOM_FIELDS = [
    # Job Applicant — AI screening block
    {"dt": JA, "fieldname": "ai_section", "fieldtype": "Section Break", "label": "AI Screening"},
    {"dt": JA, "fieldname": "ai_score", "fieldtype": "Float", "label": "AI Score", "precision": "1",
     "read_only": 1, "in_list_view": 1, "in_standard_filter": 1, "insert_after": "ai_section"},
    {"dt": JA, "fieldname": "ai_status", "fieldtype": "Select", "label": "AI Status",
     "options": "\nPending\nScored\nFailed\nSkipped", "read_only": 1, "in_standard_filter": 1,
     "insert_after": "ai_score"},
    {"dt": JA, "fieldname": "ai_col", "fieldtype": "Column Break", "insert_after": "ai_status"},
    {"dt": JA, "fieldname": "ai_model", "fieldtype": "Data", "label": "AI Model", "read_only": 1,
     "insert_after": "ai_col"},
    {"dt": JA, "fieldname": "ai_scored_on", "fieldtype": "Datetime", "label": "Scored On (UTC)",
     "read_only": 1, "insert_after": "ai_model"},
    {"dt": JA, "fieldname": "ai_detail_section", "fieldtype": "Section Break", "insert_after": "ai_scored_on"},
    {"dt": JA, "fieldname": "ai_summary", "fieldtype": "Small Text", "label": "AI Summary", "read_only": 1,
     "insert_after": "ai_detail_section"},
    {"dt": JA, "fieldname": "ai_must_have_gaps", "fieldtype": "Small Text", "label": "Must-have Gaps",
     "read_only": 1, "insert_after": "ai_summary"},
    {"dt": JA, "fieldname": "ai_reasoning", "fieldtype": "Long Text", "label": "Per-criterion Reasoning",
     "read_only": 1, "insert_after": "ai_must_have_gaps"},
    # Job Opening — rubric
    {"dt": JO, "fieldname": "ai_scoring_criteria", "fieldtype": "Long Text", "label": "AI Scoring Criteria",
     "insert_after": "description",
     "description": "One criterion per line: weight | criterion | must (optional). "
                    "Example: 30 | 3+ years building production ML systems | must"},
]


async def ensure_custom_fields(admin: Frappe):
    for f in CUSTOM_FIELDS:
        name = f"{f['dt']}-{f['fieldname']}"
        if await admin.exists("Custom Field", name):
            continue
        await admin.insert({"doctype": "Custom Field", **f})
        log.info("Created custom field %s", name)


async def ensure_service_user(admin: Frappe) -> tuple[str, str]:
    email = settings.service_user_email
    if not await admin.exists("User", email):
        await admin.insert({
            "doctype": "User",
            "email": email,
            "first_name": "AI",
            "last_name": "Screening Agent",
            "user_type": "System User",
            "send_welcome_email": 0,
            "enabled": 1,
            "roles": [{"role": "HR User"}],
        })
        log.info("Created service user %s", email)
    # Rotates the secret on every boot; only this service holds it.
    msg = await admin.call("frappe.core.doctype.user.user.generate_keys", user=email) or {}
    api_secret = msg.get("api_secret")
    api_key = msg.get("api_key") or (await admin.get_doc("User", email)).get("api_key")
    if not (api_key and api_secret):
        raise RuntimeError("Could not generate API keys for the service user")
    return api_key, api_secret


async def ensure_webhook(admin: Frappe):
    if not settings.service_public_url:
        log.warning("SERVICE_PUBLIC_URL not set; skipping webhook (use /score or the MCP rescore tool)")
        return
    url = f"{settings.service_public_url}/webhook/job-applicant"
    doc = {
        "webhook_doctype": JA,
        "webhook_docevent": "after_insert",
        "request_url": url,
        "request_method": "POST",
        "request_structure": "JSON",
        "webhook_json": '{"name": "{{ doc.name }}"}',
        "enabled": 1,
        "webhook_headers": [
            {"key": "Content-Type", "value": "application/json"},
            {"key": "X-Webhook-Token", "value": settings.webhook_token},
        ],
    }
    existing = await admin.get_list(
        "Webhook", filters=[["webhook_doctype", "=", JA], ["request_url", "like", "%/webhook/job-applicant"]]
    )
    if existing:
        await admin.update("Webhook", existing[0]["name"], doc)
    else:
        await admin.insert({"doctype": "Webhook", **doc})
        log.info("Created webhook -> %s", url)


# ---------------- demo data (fictional people) ----------------
DEMO_JOBS = [
    {
        "designation": "AI Engineer",
        "job_title": "AI Engineer — Agentic Systems",
        "description": (
            "<p>Build and operate agentic AI services for digital government. You will design LLM-based "
            "workflows, tool integrations (MCP), evaluation pipelines and on-premises model serving.</p>"
            "<ul><li>Python, FastAPI, async services</li><li>LLM serving (vLLM or similar), RAG, evaluation</li>"
            "<li>Docker/Kubernetes, CI/CD</li><li>Arabic and English communication</li></ul>"
        ),
        "criteria": (
            "30 | 3+ years building production Python services | must\n"
            "25 | Hands-on LLM application work: RAG, agents, tool calling or evaluation | must\n"
            "15 | Model serving and MLOps (vLLM, Triton, Kubernetes, GPUs)\n"
            "15 | Cloud-native delivery: Docker, CI/CD, monitoring\n"
            "15 | Clear written communication in Arabic and English"
        ),
        "applicants": [
            ("Layla Haddad", "layla.demo@example.com",
             "Senior Python engineer, 6 years. Built FastAPI microservices handling 2M requests/day at a logistics "
             "company. Since 2024 led an internal RAG assistant over 40k policy documents with hybrid search and "
             "an offline evaluation harness (faithfulness, answer relevance). Deployed Llama and Qwen models on "
             "vLLM in Kubernetes with GPU autoscaling. GitHub Actions CI, Prometheus/Grafana. Bilingual Arabic/English; "
             "wrote the team's technical design guidelines."),
            ("Omar Siddiqui", "omar.demo@example.com",
             "Data scientist, 4 years. Python, pandas, scikit-learn, XGBoost churn models. Built Streamlit dashboards. "
             "Completed online courses on LLMs and built a hobby chatbot with an API. Some Docker experience. "
             "English fluent."),
            ("Mariam Al Nuaimi (مريم)", "mariam.demo@example.com",
             "مهندسة برمجيات منذ 2019. تطوير خدمات Python و Django لبوابة خدمات حكومية. "
             "منذ 2025 أعمل على وكلاء ذكاء اصطناعي يستخدمون استدعاء الأدوات وبروتوكول MCP لربط الأنظمة الداخلية، "
             "مع بناء مجموعة اختبارات تقييم للنماذج. خبرة في Docker و GitLab CI. أكتب الوثائق الفنية بالعربية والإنجليزية."),
        ],
    },
    {
        "designation": "Data Analyst",
        "job_title": "Data Analyst — Economic Indicators",
        "description": (
            "<p>Analyse economic and business-licensing data, build dashboards for leadership and support "
            "policy teams with clear insight.</p><ul><li>SQL and Python or R</li><li>Power BI or Tableau</li>"
            "<li>Statistics and forecasting</li><li>Presenting to senior stakeholders</li></ul>"
        ),
        "criteria": (
            "30 | Strong SQL on large datasets | must\n"
            "25 | Dashboarding in Power BI or Tableau | must\n"
            "20 | Statistics, forecasting or econometrics\n"
            "15 | Python or R for analysis\n"
            "10 | Presenting insights to senior stakeholders"
        ),
        "applicants": [
            ("Khalid Rahman", "khalid.demo@example.com",
             "BI analyst, 5 years at a bank. Daily SQL on a 3 TB warehouse, built 30+ Power BI dashboards for the "
             "executive committee, monthly board presentations. ARIMA and Prophet forecasting for deposits. Python "
             "for automation."),
            ("Sara Mendes", "sara.demo@example.com",
             "Marketing coordinator, 3 years. Excel pivot tables, Google Analytics, social media reporting. "
             "Learning SQL through an online course."),
        ],
    },
]


async def _ensure(client: Frappe, doctype: str, name: str, doc: dict):
    if not await client.exists(doctype, name):
        await client.insert({"doctype": doctype, **doc})


async def seed_demo(admin: Frappe, svc: Frappe):
    if await admin.get_list(JO, limit=1):
        return  # never seed over real data
    companies = await admin.get_list("Company", limit=1)
    if not companies:
        raise RuntimeError("No Company yet — finish the Frappe setup wizard first")
    company = companies[0]["name"]

    for job in DEMO_JOBS:
        await _ensure(admin, "Designation", job["designation"], {"designation_name": job["designation"]})
        jo = await admin.insert({
            "doctype": JO,
            "job_title": job["job_title"],
            "designation": job["designation"],
            "company": company,
            "status": "Open",
            "publish": 1,
            "description": job["description"],
            "ai_scoring_criteria": job["criteria"],
        })
        for applicant_name, email, cv in job["applicants"]:
            # Created as the service user, so the webhook fires exactly as for a real applicant.
            await svc.insert({
                "doctype": JA,
                "applicant_name": applicant_name,
                "email_id": email,
                "job_title": jo["name"],
                "status": "Open",
                "cover_letter": cv,
            })
    try:
        await _ensure(admin, "Skill", "Python", {"skill_name": "Python"})
        await _ensure(admin, "Interview Round", "Technical Interview", {
            "round_name": "Technical Interview",
            "expected_skill_set": [{"skill": "Python"}],
        })
    except FrappeError as e:
        log.warning("Could not create demo interview round: %s", e)
    log.info("Seeded demo data")


async def queue_unscored(svc: Frappe):
    rows = await svc.get_list(
        JA, filters=[["ai_status", "in", ["", "Pending", "Failed"]]], limit=500
    )
    for r in rows:
        state.enqueue(r["name"])


async def bootstrap_once():
    if not settings.frappe_url:
        raise RuntimeError("FRAPPE_URL is not set")

    probe = Frappe(settings.frappe_url)
    state.phase = "waiting_for_frappe"
    while not await probe.ping():
        await asyncio.sleep(10)
    await probe.close()

    admin = None
    if settings.frappe_admin_password:
        state.phase = "configuring_frappe"
        admin = Frappe(settings.frappe_url)
        await admin.login("Administrator", settings.frappe_admin_password)
        if not await admin.get_list("Company", limit=1):
            state.phase = "waiting_for_setup_wizard"
            await admin.close()
            raise RuntimeError("Finish the Frappe setup wizard (country, company) — will retry")
        await ensure_custom_fields(admin)
        key, secret = await ensure_service_user(admin)
        await ensure_webhook(admin)
    elif settings.frappe_api_key and settings.frappe_api_secret:
        key, secret = settings.frappe_api_key, settings.frappe_api_secret
    else:
        raise RuntimeError("Set FRAPPE_ADMIN_PASSWORD (bootstrap) or FRAPPE_API_KEY/SECRET")

    if state.svc:
        await state.svc.close()
    state.svc = Frappe(settings.frappe_url, key, secret)

    if admin and settings.seed_demo:
        state.phase = "seeding_demo"
        await seed_demo(admin, state.svc)
    if admin:
        await admin.close()

    state.ready = True
    state.phase = "ready"
    await queue_unscored(state.svc)


async def bootstrap_loop():
    while True:
        try:
            await bootstrap_once()
            state.last_error = ""
            return
        except Exception as e:
            state.last_error = f"bootstrap: {e}"
            log.warning("Bootstrap not complete: %s — retrying in 60s", e)
            await asyncio.sleep(60)
