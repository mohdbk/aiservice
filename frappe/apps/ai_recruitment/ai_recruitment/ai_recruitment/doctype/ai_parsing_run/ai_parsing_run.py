import frappe
from frappe.model.document import Document


class AIParsingRun(Document):
    def after_insert(self):
        frappe.enqueue("ai_recruitment.parsing_eval.run", queue="long", timeout=3600, run_name=self.name,
                       enqueue_after_commit=True, job_id=f"ai_parse_eval::{self.name}", deduplicate=True)
