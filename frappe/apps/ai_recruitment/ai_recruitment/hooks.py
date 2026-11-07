app_name = "ai_recruitment"
app_title = "AI Recruitment"
app_publisher = "Ministry of Economy & Tourism"
app_description = "AI CV screening and an HR recruitment assistant for Frappe HR"
app_email = "digital-solutions@example.com"  # TODO: replace with the ministry contact address
app_license = "mit"

required_apps = ["hrms"]

# Chat panel on every Desk page (shown only to roles allowed in AI Recruitment Settings)
app_include_js = [
    "/assets/ai_recruitment/js/ai_markdown.js",
    "/assets/ai_recruitment/js/ai_chat.js",
]

extend_bootinfo = "ai_recruitment.setup.boot"

after_install = "ai_recruitment.setup.after_migrate"
after_migrate = "ai_recruitment.setup.after_migrate"

doc_events = {
    "Job Applicant": {
        "after_insert": "ai_recruitment.scoring.on_applicant_insert",
    }
}

scheduler_events = {
    "daily": ["ai_recruitment.governance.purge_old_logs"],
}
