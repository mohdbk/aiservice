import importlib.util
import json
import pathlib
import re
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1] / "moet_careers"
spec = importlib.util.spec_from_file_location("portal_utils", ROOT / "portal_utils.py")
u = importlib.util.module_from_spec(spec)
spec.loader.exec_module(u)
EN = json.loads((ROOT / "portal_text" / "en.json").read_text(encoding="utf-8"))
AR = json.loads((ROOT / "portal_text" / "ar.json").read_text(encoding="utf-8"))


class TestPortalUtils(unittest.TestCase):
    def test_status_codes_have_text(self):
        for code, _tone in list(u.STATUS.values()) + [u.INTERVIEW]:
            self.assertIn(f"status.{code}", EN)
        self.assertEqual(u.candidate_status("Open", True)["code"], "interview")
        self.assertEqual(u.candidate_status("Rejected", True)["code"], "not_selected")
        self.assertEqual(u.candidate_status(None)["code"], "under_review")

    def test_rtl_and_lang_codes(self):
        self.assertTrue(u.is_rtl("ar"))
        self.assertTrue(u.is_rtl("ur"))
        self.assertFalse(u.is_rtl("en"))
        self.assertTrue(u.valid_lang("zh-CN"))
        self.assertFalse(u.valid_lang("../x"))
        self.assertFalse(u.valid_lang("english"))

    def test_clean_department(self):
        self.assertEqual(u.clean_department("Digital Solutions - MOET"), "Digital Solutions")
        self.assertEqual(u.clean_department(None), "")

    def test_validate_cv(self):
        self.assertEqual(u.validate_cv("a.pdf", b"%PDF-1.7 x", 5), "pdf")
        for name, data, err in (("a.exe", b"MZ", "type"), ("a.pdf", b"", "empty"),
                                ("a.pdf", b"%PDF" + b"0" * (2 * 1024 * 1024), "size"), ("a.pdf", b"PK\x03\x04", "type")):
            with self.assertRaises(ValueError) as e:
                u.validate_cv(name, data, 1 if err == "size" else 5)
            self.assertEqual(str(e.exception), err)

    def test_phone_and_reference(self):
        self.assertTrue(u.valid_phone("+971 50 123 4567"))
        self.assertFalse(u.valid_phone("abc"))
        self.assertEqual(u.reference("HR-APP-1", "2026-11-08"), u.reference("HR-APP-1", "2026-11-08"))
        self.assertTrue(u.reference("HR-APP-1", "2026-11-08").startswith("MOET-2026-"))

    def test_pick_translation_only_approved(self):
        self.assertEqual(u.pick_translation("T", "D", None), ("T", "D"))
        self.assertEqual(u.pick_translation("T", "D", {"status": "Suggested", "job_title": "X"}), ("T", "D"))
        self.assertEqual(u.pick_translation("T", "D", {"status": "Approved", "job_title": "X", "description": ""}), ("X", "D"))

    def test_merge_text(self):
        self.assertEqual(u.merge_text({"a": "A", "b": "B"}, {"a": "x", "z": "no"}, {"b": " "}), {"a": "x", "b": "B"})

    def test_summary_text(self):
        self.assertEqual(u.summary_text("<p>Build things.</p><ul><li>SQL</li><li>Python</li></ul>"), "Build things. SQL Python")
        long = u.summary_text("<p>" + "word " * 100 + "</p>", 30)
        self.assertTrue(long.endswith("\u2026") and len(long) <= 31)

    def test_content_hash(self):
        self.assertEqual(u.content_hash("a", "b"), u.content_hash("a", "b"))
        self.assertNotEqual(u.content_hash("ab", ""), u.content_hash("a", "b"))

    def test_catalogs_complete_and_clean(self):
        self.assertEqual(set(EN), set(AR))
        for k in EN:
            self.assertEqual(set(re.findall(r"\{\w+\}", EN[k])), set(re.findall(r"\{\w+\}", AR[k])), k)
            self.assertNotIn("\u2014", EN[k] + AR[k])
            for word in ("AI", "artificial", "ذكاء"):
                self.assertNotIn(word, EN[k] + AR[k], k)

    def test_portal_fallback_copy_matches(self):
        web = pathlib.Path(__file__).resolve().parents[4] / "portal" / "web" / "src" / "text"
        if not web.exists():
            self.skipTest("portal not in this checkout")
        for lang, data in (("en", EN), ("ar", AR)):
            self.assertEqual(json.loads((web / f"{lang}.json").read_text(encoding="utf-8")), data,
                             f"copy moet_careers/portal_text/{lang}.json to portal/web/src/text/")

    def test_every_api_error_code_has_text(self):
        src = (ROOT / "portal_api.py").read_text()
        codes = (set(re.findall(r'Fail\(\s*"(\w+)"', src)) | set(re.findall(r'"(cv_\w+)"', src)) | {"forbidden"}) - {"no_logo"}  # no_logo is internal (logo proxy)
        for c in codes:
            self.assertIn(f"error.{c}", EN, c)


if __name__ == "__main__":
    unittest.main()
