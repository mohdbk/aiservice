"""The first demo used a standalone AI service with its own Frappe user, webhook and
Client Scripts. This app replaces it; disable what it left behind (idempotent)."""
import frappe

LEGACY_SERVICE_USER = "ai-agent@example.com"


def execute():
    if frappe.db.exists("User", LEGACY_SERVICE_USER):
        frappe.db.set_value("User", LEGACY_SERVICE_USER, {"enabled": 0, "api_key": None})
        frappe.db.delete("__Auth", {"doctype": "User", "name": LEGACY_SERVICE_USER, "fieldname": "api_secret"})

    for name in frappe.get_all("Webhook", filters={"request_url": ["like", "%/webhook/job-applicant"]}, pluck="name"):
        frappe.delete_doc("Webhook", name, ignore_permissions=True, force=True)

    for name in frappe.get_all("Client Script", filters={"name": ["like", "AI Assistant - %"]}, pluck="name"):
        frappe.delete_doc("Client Script", name, ignore_permissions=True, force=True)
