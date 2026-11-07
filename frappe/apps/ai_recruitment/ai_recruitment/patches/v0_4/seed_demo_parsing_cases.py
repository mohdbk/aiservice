"""Demo site only: label the five fictional demo applicants as AI Parsing Cases so a
parsing benchmark can be run straight away. Does nothing where they do not exist."""
import frappe

CASES = [
    ("layla.demo@example.com", "Layla (EN, ATS, senior engineer)", "English", 6,
     "Senior Python engineer |",
     "Python, FastAPI, RAG, hybrid search, vLLM, Kubernetes, Llama, Qwen, GitHub Actions, Prometheus, Grafana"),
    ("omar.demo@example.com", "Omar (EN, ATS, studied LLMs only)", "English", 4,
     "Data scientist |",
     "Python, pandas, scikit-learn, XGBoost, Streamlit, Docker"),
    ("mariam.demo@example.com", "Mariam (AR, ATS, Arabic CV)", "Arabic", 7.5,
     "مهندسة برمجيات |",
     "Python, Django, MCP, Docker, GitLab CI"),
    ("khalid.demo@example.com", "Khalid (EN, ATS, bank BI)", "English", 5,
     "BI analyst |",
     "SQL, Power BI, ARIMA, Prophet, Python"),
    ("sara.demo@example.com", "Sara (EN, ATS, learning SQL)", "English", 3,
     "Marketing coordinator |",
     "Excel, Google Analytics"),
]


def execute():
    if not frappe.db.table_exists("AI Parsing Case"):
        frappe.reload_doc("ai_recruitment", "doctype", "ai_parsing_case")
    for applicant, title, lang, years, roles, skills in CASES:
        if not frappe.db.exists("Job Applicant", applicant) or frappe.db.exists("AI Parsing Case", {"title": title}):
            continue
        frappe.get_doc({
            "doctype": "AI Parsing Case", "title": title, "active": 1, "language": lang, "layout": "ATS",
            "job_applicant": applicant, "expected_years": years, "expected_roles": roles, "expected_skills": skills,
        }).insert(ignore_permissions=True)
