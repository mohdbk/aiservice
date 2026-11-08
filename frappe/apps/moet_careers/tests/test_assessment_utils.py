import importlib.util
import pathlib
import unittest

spec = importlib.util.spec_from_file_location("au", pathlib.Path(__file__).resolve().parents[1] / "moet_careers" / "assessment_utils.py")
au = importlib.util.module_from_spec(spec)
spec.loader.exec_module(au)


def q(name, t="Single Choice", cat="excel", diff="Medium", points=1, **kw):
    base = {"question": name, "type": t, "text": name, "i18n": {"ar": "س " + name}, "points": points, "category": cat,
            "difficulty": diff, "options": [{"text": "A", "i18n": {"ar": "أ"}, "correct": 1}, {"text": "B", "correct": 0},
                                            {"text": "C", "correct": 0}]}
    base.update(kw)
    return base


BANK = {f"Q{i}": q(f"Q{i}", cat="excel" if i < 8 else "policy", diff="Easy" if i % 2 else "Medium") for i in range(12)}


def one(items):
    return [{"title": "Main", "minutes": 20, "items": items}]


def flat(sections):
    return au.all_questions(sections)


class TestBuild(unittest.TestCase):
    def test_fixed_and_drawn_without_duplicates(self):
        items = [{"question": "Q0"}, {"category": "excel", "count": 4}, {"category": "policy", "count": 10}]
        a = flat(au.build_attempt(one(items), BANK, "inv-1:1", True, True))
        names = [x["question"] for x in a]
        self.assertEqual(len(names), len(set(names)))
        self.assertIn("Q0", names)
        self.assertEqual(sum(1 for x in a if x["category"] == "policy"), 4)   # only 4 policy questions exist
        self.assertEqual([x["key"] for x in a], [f"q{i}" for i in range(1, len(a) + 1)])

    def test_same_seed_same_attempt_new_seed_new_attempt(self):
        items = [{"category": "excel", "count": 5}]
        a1 = au.build_attempt(one(items), BANK, "inv-1:1", True, True)
        a2 = au.build_attempt(one(items), BANK, "inv-1:1", True, True)
        self.assertEqual(a1, a2)
        seeds = {tuple(x["question"] for x in flat(au.build_attempt(one(items), BANK, f"inv-1:{n}", True, True))) for n in range(2, 8)}
        self.assertGreater(len(seeds), 1)

    def test_public_question_hides_answers(self):
        a = flat(au.build_attempt(one([{"question": "Q1"}]), BANK, "s", False, False))[0]
        a["accepted"], a["rubric"] = ["x"], "secret"
        pub = au.public_question(a, "ar")
        self.assertTrue(pub["text"].startswith("س"))
        self.assertEqual([o["text"] for o in pub["options"]], ["أ", "B", "C"])         # untranslated options fall back
        self.assertEqual(au.public_question(a, "fr")["text"], "Q1")
        self.assertNotIn("accepted", pub)
        self.assertNotIn("rubric", pub)
        self.assertTrue(all(set(o) == {"key", "text"} for o in pub["options"]))


class TestSections(unittest.TestCase):
    def test_sections_keep_order_keys_and_no_repeats(self):
        secs = [{"title": "Excel", "minutes": 10, "pass_mark": 50, "items": [{"category": "excel", "count": 3}]},
                {"title": "Policy", "minutes": 0, "items": [{"category": "policy", "count": 2}, {"question": "Q9"}]},
                {"title": "Empty", "minutes": 5, "items": [{"category": "nothing", "count": 3}]}]
        a = au.build_attempt(secs, BANK, "x:1", True, True)
        self.assertEqual([s["title"] for s in a], ["Excel", "Policy"])        # empty sections are dropped
        keys = [q["key"] for q in flat(a)]
        self.assertEqual(keys, [f"q{i}" for i in range(1, len(keys) + 1)])
        names = [q["question"] for q in flat(a)]
        self.assertEqual(len(names), len(set(names)))

    def test_section_scores_and_overall_pass(self):
        secs = [{"key": "s1", "title": "A", "pass_mark": 50, "questions": [{"key": "q1", "points": 2}, {"key": "q2", "points": 2}]},
                {"key": "s2", "title": "B", "pass_mark": 0, "questions": [{"key": "q3", "points": 6}]}]
        sc = au.section_scores(secs, {"q1": 2, "q2": 0, "q3": 6})
        self.assertEqual([(s["percent"], s["passed"]) for s in sc], [(50.0, True), (100.0, None)])
        self.assertTrue(au.passed_overall(80, 60, sc))
        sc = au.section_scores(secs, {"q1": 0, "q2": 0, "q3": 6})
        self.assertFalse(au.passed_overall(60, 60, sc))                      # failed a section with its own pass mark

    def test_section_deadline(self):
        self.assertEqual(au.section_deadline(1000, 10, 50), 1000 + 900)
        self.assertIsNone(au.section_deadline(1000, 0))


class TestScoring(unittest.TestCase):
    def test_single_and_multiple(self):
        single = flat(au.build_attempt(one([{"question": "Q1"}]), BANK, "s", False, False))[0]
        self.assertEqual(au.score_question(single, "a"), (1.0, False))
        self.assertEqual(au.score_question(single, "b"), (0.0, False))
        multi = dict(single, type="Multiple Choice", points=2,
                     options=[{"key": "a", "correct": 1}, {"key": "b", "correct": 1}, {"key": "c", "correct": 0}])
        self.assertEqual(au.score_question(multi, ["a", "b"])[0], 2.0)
        self.assertEqual(au.score_question(multi, ["a"])[0], 1.0)
        self.assertEqual(au.score_question(multi, ["a", "c"])[0], 0.0)

    def test_short_answer_arabic_normalisation_and_number(self):
        short = {"key": "q1", "type": "Short Answer", "points": 1, "accepted": ["وزارة الاقتصاد", "MoET"]}
        self.assertEqual(au.score_question(short, "  وزارة الإقتصاد ")[0], 1.0)
        self.assertEqual(au.score_question(short, "moet")[0], 1.0)
        num = {"key": "q2", "type": "Number", "points": 3, "accepted": ["12,500"], "tolerance": 50}
        self.assertEqual(au.score_question(num, "12540")[0], 3.0)
        self.assertEqual(au.score_question(num, "12600")[0], 0.0)

    def test_essay_pending_until_marked(self):
        qs = [{"key": "q1", "type": "Essay", "points": 10}, {"key": "q2", "type": "Single Choice", "points": 1,
                                                              "options": [{"key": "a", "correct": 1}]}]
        r = au.score_attempt(qs, {"q1": "My answer", "q2": "a"})
        self.assertEqual(r["pending"], ["q1"])
        r = au.score_attempt(qs, {"q1": "My answer", "q2": "a"}, {"q1": 7.5})
        self.assertEqual((r["pending"], r["earned"], r["percent"]), ([], 8.5, 77.3))
        self.assertEqual(au.score_attempt(qs, {"q2": "a"})["pending"], [])   # blank essay: nothing to mark

    def test_marks_are_clamped(self):
        qs = [{"key": "q1", "type": "Essay", "points": 5}]
        self.assertEqual(au.score_attempt(qs, {"q1": "x"}, {"q1": 99})["earned"], 5.0)


class TestTranslations(unittest.TestCase):
    def test_option_lines_and_section_text(self):
        self.assertEqual(au.option_translations([{}, {}, {}], "واحد\nاثنان"), ["واحد", "اثنان", ""])
        sec = {"title": "Excel", "instructions": "Read", "i18n": {"ar": {"title": "إكسل", "instructions": ""}}}
        self.assertEqual(au.section_text(sec, "ar"), ("إكسل", "Read"))
        self.assertEqual(au.section_text(sec, "en"), ("Excel", "Read"))


class TestRules(unittest.TestCase):
    def test_time_and_attempts(self):
        self.assertEqual(au.attempt_seconds(30, 25), 2250)
        self.assertEqual(au.attempt_seconds(0, 50), 0)
        self.assertEqual(au.can_start("Invited", 0, 1, False), "")
        self.assertEqual(au.can_start("Marked", 1, 1, False), "no_attempts_left")
        self.assertEqual(au.can_start("Marked", 1, 2, False), "")          # re-exam granted
        self.assertEqual(au.can_start("Invited", 0, 1, True), "assessment_expired")
        self.assertEqual(au.can_start("Cancelled", 0, 1, False), "assessment_cancelled")

    def test_result_policy(self):
        self.assertEqual(au.pick_result([40, 80, 60], "Highest"), 80)
        self.assertEqual(au.pick_result([40, 80, 60], "Latest"), 60)
        self.assertIsNone(au.pick_result([None], "Latest"))

    def test_clean_answer(self):
        mc = {"type": "Multiple Choice", "options": [{"key": "a"}, {"key": "b"}]}
        self.assertEqual(au.clean_answer(mc, ["b", "a", "a"]), ["a", "b"])
        with self.assertRaises(ValueError):
            au.clean_answer(mc, ["z"])
        with self.assertRaises(ValueError):
            au.clean_answer({"type": "Number"}, "abc")
        self.assertIsNone(au.clean_answer({"type": "Essay"}, ""))


if __name__ == "__main__":
    unittest.main()
