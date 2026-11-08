app_name = "moet_careers"
app_title = "MoET Careers"
app_publisher = "Ministry of Economy & Tourism"
app_description = "Public careers portal for the Ministry of Economy & Tourism, built on the UAE Design System"
app_email = "digital-solutions@example.com"  # TODO: replace with the ministry contact address
app_license = "mit"

required_apps = ["hrms", "ai_recruitment"]

# Light MoET / UAE Design System theme for the HR back office (Desk). Desk itself is not restyled.
app_include_css = ["/assets/moet_careers/css/desk_theme.css"]

after_install = "moet_careers.setup.after_migrate"
after_migrate = "moet_careers.setup.after_migrate"

website_route_rules = [
    {"from_route": "/careers/jobs/<job>", "to_route": "careers/job"},
    {"from_route": "/careers/apply/<job>", "to_route": "careers/apply"},
    {"from_route": "/careers/applications/<application>", "to_route": "careers/application"},
]
