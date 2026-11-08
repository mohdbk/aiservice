import frappe
from frappe.model.document import Document


class ScreeningQuestion(Document):
    def validate(self):
        from moet_careers.i18n import approve_rows

        self.text = (self.text or "").strip()
        self.title = self.text[:120]
        approve_rows(self.get("translations"), ("text", "options"))
        opts = [o.strip() for o in (self.options or "").splitlines() if o.strip()]
        if self.answer_type in ("Single Choice", "Multiple Choice") and len(opts) < 2:
            frappe.throw("Add at least two options, one per line.")
        self.options = "\n".join(opts)
