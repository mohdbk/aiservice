import time

import frappe
from frappe.model.document import Document


class AIOCRTest(Document):
    def after_insert(self):
        frappe.enqueue("ai_recruitment.ai_recruitment.doctype.ai_ocr_test.ai_ocr_test.run", queue="long",
                       timeout=1800, name=self.name, enqueue_after_commit=True)


# (field prefix, part of the pipeline to run)
STAGES = [("paddleocr", "paddleocr"), ("hybrid", "both")]


def run(name: str):
    from ai_recruitment.cv_reader import _file_bytes
    from ai_recruitment.ocr import read
    from ai_recruitment.utils import error_rates, word_f1

    doc = frappe.get_doc("AI OCR Test", name)
    doc.db_set({"status": "Running", "error": None}, commit=True)
    try:
        data = _file_bytes(doc.cv_file)
        ext = doc.cv_file.lower().rsplit(".", 1)[-1]
        truth = (doc.ground_truth or "")[:8000]
        out, notes = {}, []
        for key, parts in STAGES:
            t0 = time.time()
            text, how = read(data, "pdf" if ext == "pdf" else ext, parts=parts)
            if not text:
                notes.append(f"{key}: no text ({how})")
                continue
            r = {**error_rates(truth, text[:8000]), "f1": word_f1(truth, text)["f1"]}
            out.update({f"{key}_text": text[:20000], f"{key}_f1": r["f1"], f"{key}_cer": r["cer"],
                        f"{key}_wer": r["wer"], f"{key}_seconds": round(time.time() - t0, 1)})
            if key == "hybrid":
                out["recommended"] = how
        doc.db_set({**out, "status": "Completed", "error": "; ".join(notes) or None}, commit=True)
    except Exception as e:
        frappe.db.rollback()
        doc.db_set({"status": "Failed", "error": str(e)[:500]}, commit=True)
        frappe.log_error(title=f"AI OCR Test failed: {name}")
