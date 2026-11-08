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


class TestExperience(unittest.TestCase):
    def test_overlapping_periods_merged(self):
        work = [{"startDate": "2018-01", "endDate": "2020-12"},
                {"startDate": "2020-06", "endDate": "2022-12"},   # overlaps the first
                {"startDate": "bad"}]
        self.assertEqual(u.years_of_experience(work), 5.0)

    def test_present_and_formats(self):
        today = datetime(2026, 1, 15)
        work = [{"startDate": "Jan 2024", "endDate": "present"}, {"startDate": "03/2020", "endDate": "2021-02"}]
        self.assertEqual(u.years_of_experience(work, today), round((25 + 12) / 12, 1))

    def test_arabic_present(self):
        today = datetime(2026, 1, 15)
        self.assertEqual(u.years_of_experience([{"startDate": "2025-01", "endDate": "\u062d\u062a\u0649 \u0627\u0644\u0622\u0646"}], today), 1.1)


class TestResolveExperience(unittest.TestCase):
    def test_dates_preferred(self):
        self.assertEqual(u.resolve_experience(5.5, 6), (5.5, "from dated roles"))

    def test_stated_when_no_dates(self):
        self.assertEqual(u.resolve_experience(0, 6), (6.0, "stated in CV (no dates)"))

    def test_stated_when_dates_incomplete(self):
        # e.g. CV says 6 years but only one role is dated "since 2024"
        self.assertEqual(u.resolve_experience(2.0, 6), (6.0, "stated in CV (dates incomplete)"))

    def test_unknown(self):
        self.assertEqual(u.resolve_experience(0, 0), (0.0, "unknown"))

    def test_export_keeps_unnamed_employer_description(self):
        out = u.to_json_resume({"work": [{"name": "", "description": "a logistics company", "position": "Dev"}]})
        self.assertEqual(out["work"], [{"description": "a logistics company", "position": "Dev"}])


class TestLayout(unittest.TestCase):
    def test_single_column(self):
        self.assertFalse(u.looks_multicolumn([50] * 40 + [60] * 10, 600))

    def test_two_columns(self):
        self.assertTrue(u.looks_multicolumn([40] * 25 + [300] * 25, 600))


class TestJsonResume(unittest.TestCase):
    def test_mapping(self):
        prof = {"basics": {"label": "Engineer", "summary": "S"},
                "work": [{"name": "MoET", "position": "Dev", "startDate": "2020", "endDate": "", "extra": "x"}],
                "skills": [{"name": "Python", "keywords": ["FastAPI"]}]}
        out = u.to_json_resume(prof, name="A B", email="a@b.c", meta={"source": "test"})
        self.assertEqual(out["basics"]["name"], "A B")
        self.assertEqual(out["work"], [{"name": "MoET", "position": "Dev", "startDate": "2020"}])
        self.assertEqual(out["skills"][0]["keywords"], ["FastAPI"])
        self.assertEqual(out["education"], [])
        self.assertIn("jsonresume", out["$schema"])
        self.assertEqual(out["meta"]["source"], "test")


class TestErrorRates(unittest.TestCase):
    def test_identical(self):
        self.assertEqual(u.error_rates("Hello world", "hello   world"), {"cer": 0.0, "wer": 0.0})

    def test_word_error(self):
        r = u.error_rates("one two three four", "one too three four")
        self.assertEqual(r["wer"], 0.25)

    def test_arabic_diacritics_ignored(self):
        self.assertEqual(u.error_rates("\u0645\u064f\u0647\u064e\u0646\u062f\u0650\u0633", "\u0645\u0647\u0646\u062f\u0633")["cer"], 0.0)


class TestParsingMetrics(unittest.TestCase):
    CV = "Senior Python engineer, 6 years at a logistics company. FastAPI, vLLM on Kubernetes. Completed a course on LLMs."
    PROFILE = {
        "skills": [{"name": "Languages", "keywords": ["Python"]}, {"name": "Infra", "keywords": ["Kubernetes", "vLLM", "Terraform"]},
                   {"name": "FastAPI", "keywords": []}],
        "work": [{"name": "Logistics Company", "description": "", "position": "Senior Python Engineer"}],
    }

    def test_predicted_skills_use_keywords_not_categories(self):
        self.assertEqual(u.predicted_skills(self.PROFILE), ["Python", "Kubernetes", "vLLM", "Terraform", "FastAPI"])

    def test_skill_recall(self):
        r = u.skill_recall(["Python", "FastAPI", "Docker"], u.predicted_skills(self.PROFILE))
        self.assertEqual(r["recall"], round(2 / 3, 3))
        self.assertEqual(r["missing"], ["Docker"])

    def test_grounding_flags_invented_skill(self):
        g = u.grounding(self.PROFILE, self.CV)
        self.assertIn("Terraform", g["unsupported"])
        self.assertNotIn("vLLM", g["unsupported"])

    def test_role_recall_flags_invented_employer(self):
        r = u.role_recall(u.parse_role_lines("Senior Python engineer |"), self.PROFILE["work"])
        self.assertEqual(r["recall"], 0.0)
        self.assertTrue(r["wrong_employer"])

    def test_role_recall_unnamed_employer_ok(self):
        work = [{"name": "", "description": "a logistics company", "position": "Senior Python engineer"}]
        self.assertEqual(u.role_recall(u.parse_role_lines("Senior Python Engineer |"), work)["recall"], 1.0)

    def test_role_with_named_employer(self):
        work = [{"name": "Emirates NBD", "position": "BI Analyst"}]
        self.assertEqual(u.role_recall(u.parse_role_lines("BI analyst | Emirates NBD"), work)["recall"], 1.0)
        self.assertEqual(u.role_recall(u.parse_role_lines("BI analyst | ADCB"), work)["recall"], 0.0)

    def test_arabic_normalisation(self):
        self.assertEqual(u.norm_text("\u0625\u062f\u0627\u0631\u0629"), u.norm_text("\u0627\u062f\u0627\u0631\u0647"))


class TestOcrPostProcessing(unittest.TestCase):
    AR = "\u0627\u0644\u0645\u0647\u0646\u062f\u0633\u0629 \u0627\u0644\u0628\u0631\u0645\u062c\u064a\u0627\u062a \u0641\u064a \u0627\u0644\u0648\u0632\u0627\u0631\u0629"  # المهندسة البرمجيات في الوزارة

    def test_arabic_ratio(self):
        self.assertGreater(u.arabic_ratio(self.AR), 0.9)
        self.assertEqual(u.arabic_ratio("Python engineer"), 0.0)

    def test_reversed_arabic_is_fixed(self):
        reversed_text = " ".join(w[::-1] for w in self.AR.split())
        self.assertEqual(u.fix_reversed_arabic(reversed_text), self.AR)

    def test_normal_arabic_untouched(self):
        self.assertEqual(u.fix_reversed_arabic(self.AR), self.AR)
        self.assertEqual(u.fix_reversed_arabic("Python 2020 FastAPI"), "Python 2020 FastAPI")

    def test_order_lines_ltr_and_rtl(self):
        w1, w2 = "\u0645\u0647\u0646\u062f\u0633", "\u0628\u0631\u0645\u062c\u064a\u0627\u062a"
        items = [
            {"text": "Engineer", "box": [120, 10, 200, 30]}, {"text": "Senior", "box": [50, 12, 110, 30]},
            {"text": w2, "box": [50, 50, 110, 70]}, {"text": w1, "box": [120, 50, 200, 70]},  # RTL: w1 is first
        ]
        self.assertEqual(u.order_ocr_lines(items, 600), f"Senior Engineer\n{w1} {w2}")

    def test_columns_not_interleaved(self):
        items = [{"text": "Skills", "box": [20, 10, 80, 30]}, {"text": "Experience", "box": [300, 10, 400, 30]},
                 {"text": "Python", "box": [20, 40, 80, 60]}, {"text": "MoET 2020", "box": [300, 40, 400, 60]}]
        self.assertEqual(u.order_ocr_lines(items, 600).splitlines(), ["Skills", "Experience", "Python", "MoET 2020"])

    def test_repetition_loop(self):
        self.assertTrue(u.has_repetition_loop("Senior engineer at MoET\n" * 12))
        self.assertTrue(u.has_repetition_loop("Python Python Python " * 30))
        self.assertFalse(u.has_repetition_loop("Senior engineer\nPython, FastAPI\nMoET 2020 to 2024\nBSc IT"))


if __name__ == "__main__":
    unittest.main()
