import time

import frappe
from frappe.model.document import Document


class AIOCRTest(Document):
    def after_insert(self):
        frappe.enqueue("ai_recruitment.ai_recruitment.doctype.ai_ocr_test.ai_ocr_test.run", queue="long",
                       timeout=1800, name=self.name, enqueue_after_commit=True)


def run(name: str):
    from ai_recruitment.cv_reader import _file_bytes
    from ai_recruitment.llm import is_configured
    from ai_recruitment.ocr import available_engines, ocr
    from ai_recruitment.utils import error_rates

    doc = frappe.get_doc("AI OCR Test", name)
    doc.db_set({"status": "Running", "error": None}, commit=True)
    try:
        data = _file_bytes(doc.cv_file)
        ext = doc.cv_file.lower().rsplit(".", 1)[-1]
        kind = "pdf" if ext == "pdf" else ext
        truth = (doc.ground_truth or "")[:8000]
        out, scores = {}, {}
        engines = [("tesseract", "Tesseract", available_engines()["tesseract"]),
                   ("vision", "Vision Model", is_configured())]
        for key, mode, ok in engines:
            if not ok:
                continue
            t0 = time.time()
            text = ocr(data, kind, mode) or ""
            r = error_rates(truth, text[:8000])
            scores[key] = r
            out.update({f"{key}_text": text[:20000], f"{key}_cer": r["cer"], f"{key}_wer": r["wer"],
                        f"{key}_seconds": round(time.time() - t0, 1)})
        best = min(scores, key=lambda k: scores[k]["cer"]) if scores else None
        out["recommended"] = {"tesseract": "Tesseract", "vision": "Vision Model"}.get(best, "None available")
        doc.db_set({**out, "status": "Completed"}, commit=True)
    except Exception as e:
        frappe.db.rollback()
        doc.db_set({"status": "Failed", "error": str(e)[:500]}, commit=True)
        frappe.log_error(title=f"AI OCR Test failed: {name}")
