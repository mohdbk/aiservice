"""Pure logic for job screening questions (no Frappe imports, unit-tested).

Question dict: {"key": "sq1", "question": "<name>", "type": "Yes or No", "text": "...", "options": ["..."],
                "i18n": {"ar": {"text": "...", "options": ["..."]}}, "required": 1,
                "rule": "Must Be" | "At Least" | "At Most" | "", "expected": "Yes" | "3" | "Option text"}
Answers are stored in the original language (option text from the base list), so HR reads them consistently.
"""
import re
from datetime import date

TYPES = ("Yes or No", "Single Choice", "Multiple Choice", "Number", "Date", "Short Text", "Long Text")
YES, NO = "Yes", "No"


def options_for(q: dict) -> list:
    return [YES, NO] if q["type"] == "Yes or No" else list(q.get("options") or [])


def public(q: dict, lang: str) -> dict:
    """What the apply form shows: localized text and option labels, with stable option values."""
    tr = (q.get("i18n") or {}).get(lang) or {}
    base = options_for(q)
    labels = list(tr.get("options") or [])
    return {"key": q["key"], "type": q["type"], "required": bool(q.get("required")),
            "text": (tr.get("text") or "").strip() or q.get("text", ""),
            "options": [{"value": v, "label": (labels[i].strip() if i < len(labels) and labels[i].strip() else v)}
                        for i, v in enumerate(base)]}


def clean(q: dict, value):
    """Normalises one answer; returns None for empty and raises ValueError('screening_invalid') when invalid."""
    t = q["type"]
    if value in (None, "", []):
        return None
    if t in ("Yes or No", "Single Choice"):
        if value not in options_for(q):
            raise ValueError("screening_invalid")
        return value
    if t == "Multiple Choice":
        vals = value if isinstance(value, list) else [value]
        allowed = options_for(q)
        if not all(v in allowed for v in vals):
            raise ValueError("screening_invalid")
        return [v for v in allowed if v in vals]
    if t == "Number":
        s = str(value).strip().replace(",", "")
        try:
            n = float(s)
        except ValueError:
            raise ValueError("screening_invalid")
        if n < 0 or n > 1e9:
            raise ValueError("screening_invalid")
        return int(n) if n.is_integer() else n
    if t == "Date":
        s = str(value).strip()
        if not re.match(r"^\d{4}-\d{2}-\d{2}$", s):
            raise ValueError("screening_invalid")
        try:
            date.fromisoformat(s)
        except ValueError:
            raise ValueError("screening_invalid")
        return s
    if t in ("Short Text", "Long Text"):
        return str(value).strip()[: 300 if t == "Short Text" else 4000] or None
    raise ValueError("screening_invalid")


def meets(q: dict, answer) -> bool | None:
    """True or False against the job's requirement, or None when the question has no requirement."""
    rule, expected = q.get("rule") or "", str(q.get("expected") or "").strip()
    if not rule or not expected:
        return None
    if answer in (None, "", []):
        return False
    if rule == "Must Be":
        if isinstance(answer, list):
            return expected in answer
        return str(answer).strip().lower() == expected.lower()
    try:
        a, e = float(answer), float(expected.replace(",", ""))
    except (TypeError, ValueError):
        return False
    return a >= e if rule == "At Least" else a <= e if rule == "At Most" else None


def evaluate(questions: list, raw: dict) -> dict:
    """Cleans all answers and checks required questions and requirements.

    Returns {"answers": {key: value}, "missing": [keys], "invalid": [keys], "not_met": [keys]}.
    """
    answers, missing, invalid, not_met = {}, [], [], []
    for q in questions:
        try:
            v = clean(q, (raw or {}).get(q["key"]))
        except ValueError:
            invalid.append(q["key"])
            continue
        if v is None:
            if q.get("required"):
                missing.append(q["key"])
            if meets(q, None) is False:
                not_met.append(q["key"])
            continue
        answers[q["key"]] = v
        if meets(q, v) is False:
            not_met.append(q["key"])
    return {"answers": answers, "missing": missing, "invalid": invalid, "not_met": not_met}


def summary(questions: list, result: dict) -> str:
    """Plain-text answers for HR, in the questions' original language."""
    lines = []
    for q in questions:
        v = result["answers"].get(q["key"])
        shown = ", ".join(map(str, v)) if isinstance(v, list) else ("" if v is None else str(v))
        flag = " (does not meet the requirement)" if q["key"] in result["not_met"] else ""
        lines.append(f"{q.get('text_plain') or q.get('text', '')}\n  {shown or '(no answer)'}{flag}")
    return "\n".join(lines)
