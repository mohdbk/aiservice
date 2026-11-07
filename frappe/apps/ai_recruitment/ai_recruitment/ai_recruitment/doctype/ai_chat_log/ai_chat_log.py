import frappe
from frappe.model.document import Document


class AIChatLog(Document):
    def validate(self):
        # Audit records are append-only.
        if not self.is_new() and not frappe.flags.in_patch:
            frappe.throw("AI Chat Log entries cannot be edited.")
