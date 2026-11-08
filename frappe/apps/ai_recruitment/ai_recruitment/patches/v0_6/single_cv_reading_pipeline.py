"""0.6.0 keeps only the benchmark winner (PaddleOCR + vision model). Earlier modes
(Vision Model, PaddleOCR, Tesseract) map to Auto; Off stays Off."""
import frappe


def execute():
    if frappe.db.get_single_value("AI Recruitment Settings", "ocr_mode") != "Off":
        frappe.db.set_single_value("AI Recruitment Settings", "ocr_mode", "Auto")
