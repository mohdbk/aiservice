import frappe
from frappe.model.document import Document


class Assessment(Document):
    def validate(self):
        from moet_careers.assessments import validate_assessment
        validate_assessment(self)
