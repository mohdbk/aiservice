app_name = "moet_careers"
app_title = "MoET Careers"
app_publisher = "Ministry of Economy & Tourism"
app_description = "Back end of the external careers portal for the Ministry of Economy & Tourism"
app_email = "digital-solutions@example.com"  # TODO: replace with the ministry contact address
app_license = "mit"

required_apps = ["hrms", "ai_recruitment"]

# Light MoET / UAE Design System theme for the HR back office (Desk). Desk itself is not restyled.
app_include_css = ["/assets/moet_careers/css/desk_theme.css"]
doctype_js = {"Job Opening": "public/js/job_opening.js"}

after_install = "moet_careers.setup.after_migrate"
after_migrate = "moet_careers.setup.after_migrate"

doc_events = {
    "Job Opening": {"validate": "moet_careers.i18n.on_job_validate"},
}

scheduler_events = {
    "daily": ["moet_careers.accounts.purge_old_tokens"],
}
