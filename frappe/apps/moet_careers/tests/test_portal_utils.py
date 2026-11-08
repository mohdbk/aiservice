"""python -m unittest discover -s tests (no Frappe needed)"""
import importlib.util
import pathlib
import unittest

_P = pathlib.Path(__file__).resolve().parents[1] / "moet_careers" / "portal_utils.py"
_s = importlib.util.spec_from_file_location("pu", _P)
u = importlib.util.module_from_spec(_s)
_s.loader.exec_module(u)


class TestLanguage(unittest.TestCase):
    def test_arabic_default(self):
        self.assertEqual(u.pick_lang(None, None), "ar")

    def test_param_beats_cookie(self):
        self.assertEqual(u.pick_lang("en", "ar"), "en")
        self.assertEqual(u.pick_lang(None, "en"), "en")
        self.assertEqual(u.pick_lang("fr", None), "ar")

    def test_localized_fallback(self):
        self.assertEqual(u.localized("ar", "Engineer", ""), "Engineer")
        self.assertEqual(u.localized("ar", "Engineer", "مهندس"), "مهندس")
        self.assertEqual(u.localized("en", "", "مهندس"), "مهندس")


class TestStatus(unittest.TestCase):
    def test_candidate_never_sees_internal_states(self):
        self.assertEqual(u.candidate_status("Hold")["en"], "Under review")
        self.assertEqual(u.candidate_status("Rejected")["tone"], "error")
        self.assertEqual(u.candidate_status(None)["en"], "Under review")

    def test_interview(self):
        self.assertEqual(u.candidate_status("Open", has_interview=True)["en"], "Interview scheduled")
        self.assertEqual(u.candidate_status("Rejected", has_interview=True)["en"], "Not selected")


class TestCV(unittest.TestCase):
    def test_valid_types(self):
        self.assertEqual(u.validate_cv("cv.PDF", b"%PDF-1.7 ...", 5), "pdf")
        self.assertEqual(u.validate_cv("cv.docx", b"PK\x03\x04...", 5), "docx")
        self.assertEqual(u.validate_cv("p.jpg", b"\xff\xd8\xff\xe0..", 5), "jpg")

    def test_rejects_mismatch_and_size(self):
        for name, data, err in [("cv.pdf", b"MZ\x90\x00", "type"), ("cv.exe", b"%PDF", "type"),
                                ("cv.pdf", b"", "empty"), ("cv.pdf", b"%PDF" + b"0" * (2 * 1024 * 1024), "size")]:
            with self.assertRaises(ValueError) as c:
                u.validate_cv(name, data, 1 if err == "size" else 5)
            self.assertEqual(str(c.exception), err)


class TestSafety(unittest.TestCase):
    def test_redirect_only_within_careers(self):
        self.assertEqual(u.safe_redirect("/careers/apply/HR-OPN-1"), "/careers/apply/HR-OPN-1")
        for bad in ("https://evil.example", "//evil.example", "/app/job-applicant", "/careers\\@evil", None):
            self.assertEqual(u.safe_redirect(bad), "/careers")

    def test_phone(self):
        self.assertTrue(u.valid_phone("+971 50 123 4567"))
        self.assertTrue(u.valid_phone("+44 (20) 7946-0958"))
        self.assertFalse(u.valid_phone("call me"))
        self.assertFalse(u.valid_phone("12"))

    def test_department_suffix(self):
        self.assertEqual(u.clean_department("Digital Solutions - MOET"), "Digital Solutions")
        self.assertEqual(u.clean_department("Research & Development"), "Research & Development")

    def test_reference_is_stable(self):
        a = u.reference("x@example.com", "2026-11-08 10:00:00")
        self.assertEqual(a, u.reference("x@example.com", "2026-11-08 10:00:00"))
        self.assertTrue(a.startswith("MOET-2026-"))
        self.assertNotEqual(a, u.reference("y@example.com", "2026-11-08 10:00:00"))


if __name__ == "__main__":
    unittest.main()
