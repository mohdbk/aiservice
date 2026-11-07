import frappe
from frappe.model.document import Document


class AIRecruitmentSettings(Document):
    def validate(self):
        if self.llm_base_url:
            self.llm_base_url = self.llm_base_url.strip().rstrip("/")
        if self.max_cv_chars and self.max_cv_chars < 2000:
            frappe.throw("Max CV characters should be at least 2000.")
