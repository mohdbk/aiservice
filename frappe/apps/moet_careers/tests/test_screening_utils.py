import importlib.util
import pathlib
import unittest

spec = importlib.util.spec_from_file_location("su", pathlib.Path(__file__).resolve().parents[1] / "moet_careers" / "screening_utils.py")
su = importlib.util.module_from_spec(spec)
spec.loader.exec_module(su)

Q = [
    {"key": "sq1", "type": "Yes or No", "text": "Do you hold a UAE driving licence?", "required": 1, "rule": "Must Be", "expected": "Yes",
     "i18n": {"ar": {"text": "هل لديك رخصة قيادة إماراتية؟", "options": ["نعم", "لا"]}}},
    {"key": "sq2", "type": "Number", "text": "Years of experience", "required": 1, "rule": "At Least", "expected": "3"},
    {"key": "sq3", "type": "Multiple Choice", "text": "Languages", "options": ["Arabic", "English", "French"], "rule": "Must Be", "expected": "Arabic"},
    {"key": "sq4", "type": "Date", "text": "Earliest start date"},
    {"key": "sq5", "type": "Short Text", "text": "Notice period"},
]


class TestScreening(unittest.TestCase):
    def test_public_localizes_labels_but_keeps_values(self):
        p = su.public(Q[0], "ar")
        self.assertEqual(p["text"], "هل لديك رخصة قيادة إماراتية؟")
        self.assertEqual([(o["value"], o["label"]) for o in p["options"]], [("Yes", "نعم"), ("No", "لا")])
        self.assertEqual(su.public(Q[0], "fr")["options"][0]["label"], "Yes")
        self.assertNotIn("expected", p)
        self.assertNotIn("rule", p)

    def test_all_good(self):
        r = su.evaluate(Q, {"sq1": "Yes", "sq2": "5", "sq3": ["English", "Arabic"], "sq4": "2027-01-15", "sq5": " 1 month "})
        self.assertEqual((r["missing"], r["invalid"], r["not_met"]), ([], [], []))
        self.assertEqual(r["answers"]["sq3"], ["Arabic", "English"])
        self.assertEqual(r["answers"]["sq2"], 5)

    def test_requirements_flag_but_do_not_reject(self):
        r = su.evaluate(Q, {"sq1": "No", "sq2": "2", "sq3": ["French"]})
        self.assertEqual(sorted(r["not_met"]), ["sq1", "sq2", "sq3"])
        self.assertEqual(r["missing"], [])
        self.assertIn("does not meet", su.summary(Q, r))

    def test_missing_and_invalid(self):
        r = su.evaluate(Q, {"sq1": "Maybe", "sq4": "15/01/2027"})
        self.assertEqual(sorted(r["invalid"]), ["sq1", "sq4"])
        self.assertEqual(r["missing"], ["sq2"])
        with self.assertRaises(ValueError):
            su.clean(Q[1], "-4")

    def test_meets_rules(self):
        self.assertIsNone(su.meets(Q[3], "2027-01-01"))
        self.assertTrue(su.meets({"rule": "At Most", "expected": "10"}, 7))
        self.assertTrue(su.meets({"rule": "Must Be", "expected": "yes"}, "Yes"))


if __name__ == "__main__":
    unittest.main()
