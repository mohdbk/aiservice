import frappe
from frappe.model.document import Document


class AIEvaluationRun(Document):
    def validate(self):
        self.sample_size = max(1, min(int(self.sample_size or 5), 20))
        self.repeats = max(2, min(int(self.repeats or 2), 5))

    def after_insert(self):
        frappe.enqueue(
            "ai_recruitment.evaluation.run",
            queue="long",
            timeout=3600,
            run_name=self.name,
            enqueue_after_commit=True,
            job_id=f"ai_eval::{self.name}",
            deduplicate=True,
        )
