import frappe
from frappe.model.document import Document


class AssessmentQuestion(Document):
    def validate(self):
        from moet_careers.assessments import validate_question
        validate_question(self)
