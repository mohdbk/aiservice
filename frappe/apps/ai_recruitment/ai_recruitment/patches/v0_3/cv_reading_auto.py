"""0.3.0: 'Tesseract' was only the default OCR mode; switch it to Auto (vision for scanned and
designed CVs, Tesseract fallback). An admin's explicit 'Vision Model' or 'Off' is kept."""
import frappe


def execute():
    if frappe.db.get_single_value("AI Recruitment Settings", "ocr_mode") in (None, "", "Tesseract"):
        frappe.db.set_single_value("AI Recruitment Settings", "ocr_mode", "Auto")
