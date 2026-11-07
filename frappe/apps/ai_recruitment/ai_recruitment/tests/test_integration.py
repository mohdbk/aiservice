"""Integration tests (need a bench site with erpnext + hrms + ai_recruitment). The LLM is mocked.

    bench --site test_site set-config allow_tests true
    bench --site test_site run-tests --app ai_recruitment
"""
import json
from unittest.mock import patch

import frappe

try:
    from frappe.tests import IntegrationTestCase as TestCase
except ImportError:  # older v15
    from frappe.tests.utils import FrappeTestCase as TestCase

from ai_recruitment import agent, profile_parser, scoring

RUBRIC = "60 | Python services | must\n40 | LLM work"
CV = "Senior engineer. Eight years of Python services. Built RAG assistants with evaluation. " * 3


def _score_reply(scores):
    return {"role": "assistant", "content": json.dumps({
        "criteria": [{"criterion": f"c{i}", "weight": 1, "score": s, "evidence": "e \u2014 x"} for i, s in enumerate(scores)],
        "summary": "Strong \u2014 fit.", "summary_ar": "مناسب",
    })}


PROFILE = {"role": "assistant", "content": json.dumps({
    "basics": {"label": "Engineer", "summary": "Python"},
    "work": [{"name": "Org", "position": "Dev", "startDate": "2018-01", "endDate": "2020-12", "summary": "", "highlights": []}],
    "education": [], "skills": [{"name": "Python", "keywords": ["FastAPI"]}], "languages": [],
    "certificates": [], "projects": [], "cv_language": "en"})}


def _company():
    return frappe.get_all("Company", pluck="name", limit_page_length=1)[0]


class TestAIRecruitment(TestCase):
    @classmethod
    def setUpClass(cls):
        super().setUpClass()
        if not frappe.db.exists("Designation", "AI Test Engineer"):
            frappe.get_doc({"doctype": "Designation", "designation_name": "AI Test Engineer"}).insert()
        cls.job = frappe.get_doc({
            "doctype": "Job Opening", "job_title": "AI Test Role", "designation": "AI Test Engineer",
            "company": _company(), "status": "Open", "description": "Python and LLMs",
            "ai_scoring_criteria": RUBRIC,
        }).insert()
        s = frappe.get_single("AI Recruitment Settings")
        s.enabled, s.auto_score, s.chat_enabled = 1, 0, 1
        s.llm_base_url, s.llm_model = "http://llm.test/v1", "test-model"
        s.save()

    def _applicant(self, name="Test Candidate"):
        return frappe.get_doc({"doctype": "Job Applicant", "applicant_name": name,
                               "email_id": f"{frappe.generate_hash(length=8)}@example.com",
                               "job_title": self.job.name, "cover_letter": CV}).insert()

    def test_scoring_writes_weighted_score_without_em_dash(self):
        app = self._applicant()
        with patch.object(scoring, "chat_completion", return_value=_score_reply([10, 5])), \
                patch.object(profile_parser, "chat_completion", return_value=PROFILE):
            res = scoring.score_applicant(app.name)
        app.reload()
        self.assertEqual(res["status"], "Scored")
        self.assertEqual(app.ai_score, 80.0)  # 60*10 + 40*5 over 1000 -> 80
        self.assertEqual(app.ai_status, "Scored")
        self.assertEqual(app.ai_years_experience, 3.0)
        self.assertEqual(json.loads(app.ai_profile)["skills"][0]["name"], "Python")
        self.assertNotIn("\u2014", app.ai_summary + app.ai_reasoning)

    def test_must_have_gap(self):
        app = self._applicant("Gap Candidate")
        with patch.object(scoring, "chat_completion", return_value=_score_reply([2, 9])), \
                patch.object(profile_parser, "chat_completion", return_value=PROFILE):
            scoring.score_applicant(app.name)
        app.reload()
        self.assertIn("Python services", app.ai_must_have_gaps)

    def test_redaction_before_model(self):
        app = self._applicant("Unique Surnameabc")
        seen = {}

        def capture(messages, **kw):
            seen["prompt"] = messages[-1]["content"]
            return _score_reply([5, 5])

        with patch.object(scoring, "chat_completion", side_effect=capture):
            scoring.compute_score(app, self.job, f"Unique Surnameabc, {app.email_id}\nNationality: X\n{CV}")
        self.assertNotIn("Surnameabc", seen["prompt"])
        self.assertNotIn(app.email_id, seen["prompt"])
        self.assertIn("Nationality: [REDACTED]", seen["prompt"])

    def test_proposal_requires_approval_and_is_single_use(self):
        app = self._applicant("Approval Candidate")
        proposal = agent._propose("set_applicant_status", {"applicant": app.name, "status": "Hold"}, "sess1")
        self.assertEqual(frappe.db.get_value("Job Applicant", app.name, "status"), "Open")  # not applied yet
        agent.approve(proposal["id"])
        self.assertEqual(frappe.db.get_value("Job Applicant", app.name, "status"), "Hold")
        with self.assertRaises(frappe.ValidationError):
            agent.approve(proposal["id"])  # single use
        self.assertTrue(frappe.db.exists("AI Chat Log", {"log_type": "Approval", "session_id": "sess1"}))

    def test_disallowed_status_is_refused(self):
        app = self._applicant("Reject Candidate")
        with self.assertRaises(frappe.ValidationError):
            agent._propose("set_applicant_status", {"applicant": app.name, "status": "Rejected"}, "s")

    def test_chat_turn_is_logged_and_em_dash_removed(self):
        reply = ({"role": "assistant", "content": "Done \u2014 here."}, {"prompt_tokens": 10, "completion_tokens": 5})
        with patch.object(agent, "complete", return_value=reply):
            res = agent.run("hello", "sess2", {"route": "app"})
        self.assertEqual(res["reply"], "Done, here.")
        log = frappe.get_last_doc("AI Chat Log", filters={"session_id": "sess2"})
        self.assertEqual(log.status, "Success")
        self.assertEqual(log.prompt_tokens, 10)

    def test_json_resume_export(self):
        from ai_recruitment.api import export_json_resume

        app = self._applicant("Export Candidate")
        with patch.object(scoring, "chat_completion", return_value=_score_reply([7, 7])), \
                patch.object(profile_parser, "chat_completion", return_value=PROFILE):
            scoring.score_applicant(app.name)
        out = export_json_resume(app.name)
        self.assertEqual(out["basics"]["name"], "Export Candidate")
        self.assertEqual(out["work"][0]["name"], "Org")
        self.assertIn("jsonresume", out["$schema"])

    def test_parsing_run_measures_accuracy_and_grounding(self):
        from ai_recruitment import parsing_eval

        case = frappe.get_doc({
            "doctype": "AI Parsing Case", "title": "Test case", "language": "English", "layout": "ATS",
            "cv_text": "Data scientist, 4 years at a bank. Python, pandas, XGBoost. Completed a course on LLMs. " * 2,
            "expected_years": 4, "expected_roles": "Data scientist |", "expected_skills": "Python, pandas, XGBoost",
        }).insert()
        reply = {"role": "assistant", "content": json.dumps({
            "basics": {"label": "", "summary": ""},
            "work": [{"name": "", "description": "a bank", "position": "Data scientist", "startDate": "", "endDate": "",
                      "summary": "", "highlights": []}],
            "education": [], "skills": [{"name": "ML", "keywords": ["Python", "pandas", "LLMs", "Terraform"]}],
            "languages": [], "certificates": [], "projects": [], "stated_years_of_experience": 4, "cv_language": "en"})}
        run = frappe.get_doc({"doctype": "AI Parsing Run"})
        run.insert()
        frappe.db.set_value("AI Parsing Case", {"name": ["!=", case.name]}, "active", 0)
        with patch.object(profile_parser, "chat_completion", return_value=reply):
            parsing_eval.run(run.name)
        run.reload()
        self.assertEqual(run.status, "Completed")
        self.assertEqual(run.years_accuracy, 100.0)
        self.assertEqual(run.role_recall, 100.0)
        self.assertEqual(run.skill_recall, round(2 / 3 * 100, 1))    # XGBoost missed
        self.assertEqual(run.grounding, 75.0)                          # Terraform is not in the CV
        self.assertIn("Terraform", run.report)
