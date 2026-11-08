import frappe
from frappe.model.document import Document

from moet_careers.portal_utils import valid_lang


class MoETCareersSettings(Document):
    def validate(self):
        if self.max_cv_mb and not (1 <= self.max_cv_mb <= 20):
            frappe.throw("Max CV size must be between 1 and 20 MB.")
        url = (self.main_site_url or "").strip()
        if url and not url.startswith(("https://", "http://")):
            frappe.throw("Ministry Website must start with https://")
        seen = set()
        for row in self.languages or []:
            if not valid_lang(row.language):
                frappe.throw(f"{row.language} cannot be used in portal addresses (use codes such as ar, en, fr).")
            if row.language in seen:
                frappe.throw(f"{row.language} is listed twice.")
            seen.add(row.language)
        if not self.default_language:
            self.default_language = "ar"
        if self.default_language not in seen:
            self.append("languages", {"language": self.default_language, "enabled": 1})
        for row in self.languages:
            if row.language == self.default_language:
                row.enabled = 1
