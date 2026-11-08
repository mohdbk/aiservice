import time

import frappe
from frappe.model.document import Document


class AIOCRTest(Document):
    def after_insert(self):
        frappe.enqueue("ai_recruitment.ai_recruitment.doctype.ai_ocr_test.ai_ocr_test.run", queue="long",
                       timeout=1800, name=self.name, enqueue_after_commit=True)


ENGINES = [("tesseract", "Tesseract", "Tesseract"), ("paddleocr", "PaddleOCR", "PaddleOCR"),
           ("vision", "Vision Model", "Vision Model"), ("hybrid", "Hybrid", "Hybrid (Auto)")]


def run(name: str):
    from ai_recruitment.cv_reader import _file_bytes
    from ai_recruitment.llm import is_configured
    from ai_recruitment.ocr import available_engines, read
    from ai_recruitment.utils import error_rates, word_f1

    doc = frappe.get_doc("AI OCR Test", name)
    doc.db_set({"status": "Running", "error": None}, commit=True)
    try:
        data = _file_bytes(doc.cv_file)
        ext = doc.cv_file.lower().rsplit(".", 1)[-1]
        truth = (doc.ground_truth or "")[:8000]
        avail = available_engines()
        ok = {"tesseract": avail["tesseract"], "paddleocr": avail["paddle"],
              "vision": is_configured(), "hybrid": is_configured()}
        out, scores, errors = {}, {}, []
        for key, mode, label in ENGINES:
            if not ok[key]:
                continue
            t0 = time.time()
            try:
                text = read(data, "pdf" if ext == "pdf" else ext, mode)[0] or ""
            except Exception as e:
                errors.append(f"{label}: {str(e)[:150]}")
                continue
            r = {**error_rates(truth, text[:8000]), "f1": word_f1(truth, text)["f1"]}
            scores[label] = r
            out.update({f"{key}_text": text[:20000], f"{key}_f1": r["f1"], f"{key}_cer": r["cer"],
                        f"{key}_wer": r["wer"], f"{key}_seconds": round(time.time() - t0, 1)})
        out["recommended"] = max(scores, key=lambda k: scores[k]["f1"]) if scores else "None available"
        doc.db_set({**out, "status": "Completed", "error": "; ".join(errors) or None}, commit=True)
    except Exception as e:
        frappe.db.rollback()
        doc.db_set({"status": "Failed", "error": str(e)[:500]}, commit=True)
        frappe.log_error(title=f"AI OCR Test failed: {name}")
