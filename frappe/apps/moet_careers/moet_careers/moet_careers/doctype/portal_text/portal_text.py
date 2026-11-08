import frappe
from frappe.model.document import Document

from moet_careers.portal_utils import content_hash


class PortalText(Document):
    def validate(self):
        from moet_careers.i18n import base_text

        base = base_text("en")
        if self.key not in base:
            frappe.throw(f"Unknown portal text key: {self.key}")
        self.source_text = base[self.key]
        self.text = (self.text or "").strip()
        if frappe.db.exists("Portal Text", {"language": self.language, "key": self.key, "name": ["!=", self.name]}):
            frappe.throw(f"There is already a {self.language} text for {self.key}.")
        # Editing a suggestion approves it
        if self.status == "Suggested" and self.suggested_hash and content_hash(self.text) != self.suggested_hash:
            self.status = "Approved"

    def on_update(self):
        frappe.cache.delete_value(f"moet_portal_text::{self.language}")

    def on_trash(self):
        frappe.cache.delete_value(f"moet_portal_text::{self.language}")
