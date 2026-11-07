"""AI Parsing Run: parse every active AI Parsing Case and measure accuracy.

Uses the production reading and parsing path (read_cv -> redact -> parse_profile).
Metrics (fractions 0-1; stored as percentages):
- years accuracy:   share of cases with years of experience within +/- tolerance
- roles found:      share of labelled roles found (an invented employer counts as not found)
- skills found:     share of labelled skills found
- grounding:        share of extracted skills/employers/institutions that appear in the CV text
- language:         share of cases with the CV language detected correctly
"""
import json
import re
import statistics

import frappe
from frappe.utils import now_datetime

from ai_recruitment.utils import grounding, parse_role_lines, predicted_skills, redact, role_recall, skill_recall

LANG = {"Arabic": "ar", "English": "en", "Mixed": "mixed"}


def _case_text(case) -> tuple:
    from ai_recruitment.cv_reader import read_cv

    if case.cv_file:
        r = read_cv(frappe._dict(resume_attachment=case.cv_file, cover_letter=case.cv_text or "",
                                 applicant_name=case.candidate_name or "", name=case.name))
        return r["text"], f"{r['layout']} / {r['method']}", case.candidate_name or ""
    if case.job_applicant:
        app = frappe.get_doc("Job Applicant", case.job_applicant)
        r = read_cv(app)
        return r["text"], f"{r['layout']} / {r['method']}", app.applicant_name
    return case.cv_text or "", "pasted text", case.candidate_name or ""


def _mean(vals):
    vals = [v for v in vals if v is not None]
    return round(statistics.mean(vals), 3) if vals else None


def run(run_name: str):
    from ai_recruitment.llm import config
    from ai_recruitment.profile_parser import parse_profile

    ev = frappe.get_doc("AI Parsing Run", run_name)
    ev.db_set({"status": "Running", "started_on": now_datetime(), "error": None}, commit=True)
    try:
        filters = {"active": 1}
        if ev.language:
            filters["language"] = ev.language
        if ev.layout:
            filters["layout"] = ev.layout
        cases = frappe.get_all("AI Parsing Case", filters=filters, pluck="name", order_by="creation asc")
        tol = float(ev.years_tolerance if ev.years_tolerance is not None else 1)
        rows = []
        for name in cases:
            case = frappe.get_doc("AI Parsing Case", name)
            try:
                text, read_as, cand = _case_text(case)
                if len(text.strip()) < 50:
                    rows.append({"case": name, "title": case.title, "error": "no readable CV text", "read_as": read_as})
                    continue
                profile = parse_profile(redact(text, cand)[: int(config().settings.max_cv_chars or 40000)])
                exp_skills = [s.strip() for s in re.split(r"[,\n]", case.expected_skills or "") if s.strip()]
                sk = skill_recall(exp_skills, predicted_skills(profile))
                rl = role_recall(parse_role_lines(case.expected_roles or ""), profile.get("work") or [])
                gr = grounding(profile, text)
                yrs = profile.get("years_of_experience")
                yrs_ok = None if not case.expected_years else abs(float(yrs or 0) - case.expected_years) <= tol
                lang_ok = (profile.get("cv_language") == LANG.get(case.language))
                rows.append({
                    "case": name, "title": case.title, "language": case.language, "layout": case.layout,
                    "read_as": read_as, "years": yrs, "years_basis": profile.get("years_basis"),
                    "expected_years": case.expected_years, "years_ok": yrs_ok,
                    "role_recall": rl["recall"], "wrong_employer": rl["wrong_employer"],
                    "skill_recall": sk["recall"], "missing_skills": sk["missing"],
                    "grounding": gr["rate"], "unsupported": gr["unsupported"],
                    "language_ok": lang_ok, "detected_language": profile.get("cv_language"),
                })
            except Exception as e:
                rows.append({"case": name, "title": case.title, "error": str(e)[:300]})
            frappe.db.commit()

        ok = [r for r in rows if "error" not in r]
        metrics = {
            "years_accuracy": _mean([1.0 if r["years_ok"] else 0.0 for r in ok if r["years_ok"] is not None]),
            "role_recall": _mean([r["role_recall"] for r in ok]),
            "skill_recall": _mean([r["skill_recall"] for r in ok]),
            "grounding": _mean([r["grounding"] for r in ok]),
            "language_accuracy": _mean([1.0 if r["language_ok"] else 0.0 for r in ok]),
        }
        thresholds = {"years_accuracy": ev.min_years_accuracy, "role_recall": ev.min_role_recall,
                      "skill_recall": ev.min_skill_recall, "grounding": ev.min_grounding,
                      "language_accuracy": ev.min_language_accuracy}
        checks = {k: (metrics[k] is None or metrics[k] >= float(thresholds[k] or 0)) for k in metrics}
        passed = bool(ok) and all(checks.values())

        ev.db_set({
            "status": "Completed", "passed": 1 if passed else 0, "cases_tested": len(ok),
            **{k: (round(v * 100, 1) if v is not None else None) for k, v in metrics.items()},
            "model": frappe.get_cached_doc("AI Recruitment Settings").llm_model, "finished_on": now_datetime(),
            "report": _report(rows, ok, metrics, thresholds, checks, passed, tol),
            "details": json.dumps(rows, indent=1, ensure_ascii=False, default=str),
        }, commit=True)
    except Exception as e:
        frappe.db.rollback()
        ev.db_set({"status": "Failed", "error": str(e)[:500], "finished_on": now_datetime()}, commit=True)
        frappe.log_error(title=f"AI Parsing Run failed: {run_name}")


def _pct(v):
    return "n/a" if v is None else f"{v * 100:.0f}%"


def _report(rows, ok, metrics, thresholds, checks, passed, tol) -> str:
    labels = {"years_accuracy": f"Years within +/-{tol:g}", "role_recall": "Roles found",
              "skill_recall": "Skills found", "grounding": "Grounding (nothing invented)",
              "language_accuracy": "Language detected"}
    lines = [f"## {'PASSED' if passed else 'FAILED'}", f"Cases tested: {len(ok)} of {len(rows)}.", "",
             "| Metric | Result | Minimum | |", "|---|---|---|---|"]
    for k, lab in labels.items():
        lines.append(f"| {lab} | {_pct(metrics[k])} | {_pct(float(thresholds[k] or 0))} | {'PASS' if checks[k] else 'FAIL'} |")

    segments = {}
    for r in ok:
        for seg in (f"Language: {r['language']}", f"Layout: {r['layout']}"):
            segments.setdefault(seg, []).append(r)
    if len(segments) > 2:
        lines += ["", "### By segment", "", "| Segment | Cases | Years | Roles | Skills | Grounding |", "|---|---|---|---|---|---|"]
        for seg, rs in sorted(segments.items()):
            lines.append(
                f"| {seg} | {len(rs)} | {_pct(_mean([1.0 if r['years_ok'] else 0.0 for r in rs if r['years_ok'] is not None]))} | "
                f"{_pct(_mean([r['role_recall'] for r in rs]))} | {_pct(_mean([r['skill_recall'] for r in rs]))} | "
                f"{_pct(_mean([r['grounding'] for r in rs]))} |")

    lines += ["", "### Cases", "", "| Case | Read as | Years (expected) | Roles | Skills | Grounding | Issues |",
              "|---|---|---|---|---|---|---|"]
    for r in rows:
        if "error" in r:
            lines.append(f"| {r['title']} | {r.get('read_as', '')} | | | | | Error: {r['error']} |")
            continue
        issues = []
        if r["missing_skills"]:
            issues.append("missing skills: " + ", ".join(r["missing_skills"][:6]))
        if r["unsupported"]:
            issues.append("not in CV: " + ", ".join(r["unsupported"][:6]))
        if r["wrong_employer"]:
            issues.append("employer: " + "; ".join(r["wrong_employer"][:3]))
        if not r["language_ok"]:
            issues.append(f"language detected as {r['detected_language']}")
        exp = f" ({r['expected_years']:g})" if r["expected_years"] else ""
        lines.append(f"| {r['title']} | {r['read_as']} | {r['years']}{exp} | {_pct(r['role_recall'])} | "
                     f"{_pct(r['skill_recall'])} | {_pct(r['grounding'])} | {'; '.join(issues) or 'none'} |")
    lines += ["", "Grounding below 100% means the parser produced something the CV does not contain. "
                  "Check those items first: they are the ones that can unfairly raise a score."]
    return "\n".join(lines)
