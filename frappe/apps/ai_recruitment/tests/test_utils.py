"""Pure unit tests (no Frappe needed): python -m unittest discover -s tests"""
import importlib.util
import pathlib
import unittest
from datetime import datetime

_UTILS = pathlib.Path(__file__).resolve().parents[1] / "ai_recruitment" / "utils.py"
_spec = importlib.util.spec_from_file_location("ai_utils", _UTILS)
u = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(u)


class TestNoEmDash(unittest.TestCase):
    def test_em_dash_replaced(self):
        self.assertEqual(u.no_em_dash("Strong \u2014 6 years Python"), "Strong, 6 years Python")

    def test_spaced_en_dash_replaced_but_ranges_kept(self):
        self.assertEqual(u.no_em_dash("2018–2023 role – senior"), "2018–2023 role, senior")

    def test_leading_dash_line(self):
        self.assertEqual(u.no_em_dash("\u2014 item"), "item")

    def test_clean_recursive(self):
        out = u.clean({"a": ["x \u2014 y", {"b": "p\u2014q"}], "n": 3})
        self.assertEqual(out, {"a": ["x, y", {"b": "p, q"}], "n": 3})

    def test_writing_rule_has_no_dash_chars(self):
        self.assertNotIn("\u2014", u.WRITING_RULE)


class TestRedact(unittest.TestCase):
    def test_name_email_phone(self):
        out = u.redact("Layla Haddad, layla@x.com, +971 50 123 4567. Worked 2018 - 2023.", "Layla Haddad")
        self.assertNotIn("Layla", out)
        self.assertNotIn("Haddad", out)
        self.assertIn("[EMAIL]", out)
        self.assertIn("[PHONE]", out)
        self.assertIn("2018 - 2023", out)  # year ranges are not phone numbers

    def test_labelled_attributes(self):
        out = u.redact("Nationality: X\nGender: F\nAge: 40\nAgency: MoET\nReligion: Y", "")
        self.assertIn("Nationality: [REDACTED]", out)
        self.assertIn("Gender: [REDACTED]", out)
        self.assertIn("Age: [REDACTED]", out)
        self.assertIn("Agency: MoET", out)
        self.assertIn("Religion: [REDACTED]", out)

    def test_arabic_labels_and_name(self):
        out = u.redact("الجنسية: سعودي\nمريم مهندسة",
                       "مريم")
        self.assertIn("[REDACTED]", out)
        self.assertIn("[CANDIDATE]", out)


class TestRubric(unittest.TestCase):
    RUBRIC = "30 | Python | must\n# comment\n20 | Cloud\nbad line\n0 | ignored\n50 | LLM | required"

    def test_parse(self):
        r = u.parse_criteria(self.RUBRIC)
        self.assertEqual([c["criterion"] for c in r], ["Python", "Cloud", "LLM"])
        self.assertEqual([c["must"] for c in r], [True, False, True])

    def test_weights_from_rubric_not_model(self):
        rubric = u.parse_criteria(self.RUBRIC)
        items = [{"score": 10, "weight": 999}, {"score": 0, "weight": 999}, {"score": 4, "weight": 999}]
        res = u.combine_scores(rubric, items)
        self.assertEqual(res["total"], round((30 * 10 + 20 * 0 + 50 * 4) / (10 * 100) * 100, 1))
        self.assertEqual(res["gaps"], ["LLM"])

    def test_scores_clamped_and_missing_items(self):
        rubric = u.parse_criteria("50 | A\n50 | B")
        res = u.combine_scores(rubric, [{"score": 99}])
        self.assertEqual(res["rows"][0]["score"], 10)
        self.assertEqual(res["rows"][1]["score"], 0)
        self.assertEqual(res["total"], 50.0)

    def test_no_rubric_uses_model_weights(self):
        res = u.combine_scores([], [{"criterion": "X", "weight": 1, "score": 5, "evidence": "e"}])
        self.assertEqual(res["total"], 50.0)

    def test_reasoning_has_no_em_dash(self):
        res = u.combine_scores(u.parse_criteria("10 | A"), [{"score": 5, "evidence": "ok"}])
        self.assertNotIn("\u2014", res["reasoning"])


class TestJson(unittest.TestCase):
    def test_fenced(self):
        self.assertEqual(u.extract_json('```json\n{"a": 1}\n```'), {"a": 1})

    def test_embedded(self):
        self.assertEqual(u.extract_json('Here: {"a": 2} thanks'), {"a": 2})

    def test_invalid(self):
        with self.assertRaises(ValueError):
            u.extract_json("no json")


class TestScheduling(unittest.TestCase):
    def test_free_slots_skip_busy(self):
        busy = [(datetime(2026, 11, 10, 10, 0), datetime(2026, 11, 10, 11, 0))]
        slots = u.free_slots("2026-11-10", busy, "09:00", "12:00", 60, 30)
        self.assertEqual(slots, ["09:00-10:00", "11:00-12:00"])

    def test_ics(self):
        ics = u.ics_event("x@y", datetime(2026, 11, 10, 9), datetime(2026, 11, 10, 10), "Interview; Tech", "line1\nline2")
        self.assertIn("DTSTART;TZID=Asia/Dubai:20261110T090000", ics)
        self.assertIn("SUMMARY:Interview\\; Tech", ics)
        self.assertTrue(ics.endswith("END:VCALENDAR\r\n"))


class TestEvaluationVariants(unittest.TestCase):
    def test_variants(self):
        v = u.fairness_variants("CV text", seed=1)
        self.assertEqual(len(v), 3)
        self.assertTrue(all("CV text" in t for _, t in v))
        self.assertIn("ignore all previous", u.injection_variant("CV"))

    def test_labelled_attributes_are_redacted(self):
        _, labelled = u.fairness_variants("CV text")[2]
        out = u.redact(labelled, "")
        self.assertNotIn("Female", out)
        self.assertNotIn("58", out)


if __name__ == "__main__":
    unittest.main()
