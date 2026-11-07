import frappe
from frappe.model.document import Document


class AIParsingCase(Document):
    def validate(self):
        if not (self.cv_file or self.job_applicant or (self.cv_text or "").strip()):
            frappe.throw("Attach a CV file, link a Job Applicant, or paste the CV text.")
        if not (self.expected_roles or self.expected_skills or self.expected_years):
            frappe.throw("Add at least one correct answer: years, roles or skills.")
