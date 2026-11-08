import frappe
from frappe.model.document import Document


class ScreeningQuestion(Document):
    def validate(self):
        from moet_careers.bilingual import sync
        from moet_careers.i18n import approve_rows

        self.text = (self.text or "").strip()
        self.title = self.text[:120]
        opts = [o.strip() for o in (self.options or "").splitlines() if o.strip()]
        if self.answer_type in ("Single Choice", "Multiple Choice") and len(opts) < 2:
            frappe.throw("Add at least two options, one per line.")
        self.options = "\n".join(opts)
        if self.answer_type not in ("Single Choice", "Multiple Choice"):
            self.options_en = ""
        elif self.options_en:
            ar = [o.strip() for o in self.options_en.splitlines() if o.strip()]
            if len(ar) != len(opts):
                frappe.throw(f"Options (English) has {len(ar)} lines but Options has {len(opts)}. Use one line per "
                             "option, in the same order.")
        sync(self)
        approve_rows(self.get("translations"), ("text", "options"))
