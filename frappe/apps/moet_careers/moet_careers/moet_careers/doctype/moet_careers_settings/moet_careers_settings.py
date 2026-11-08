import frappe
from frappe.model.document import Document


class MoETCareersSettings(Document):
    def validate(self):
        if self.max_cv_mb and not (1 <= self.max_cv_mb <= 20):
            frappe.throw("Max CV size must be between 1 and 20 MB.")
        for field in ("main_site_url", "privacy_url", "accessibility_url", "terms_url", "contact_url"):
            url = (self.get(field) or "").strip()
            if url and not url.startswith(("https://", "http://", "/")):
                frappe.throw(f"{self.meta.get_label(field)} must start with https://")
