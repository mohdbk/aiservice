"""Pure assessment logic (no Frappe imports, unit-tested): building an attempt, scoring, timing, re-exams.

Question dicts (as stored in an attempt snapshot). Text in other languages comes from approved translations:
  {"key": "q1", "question": "<name>", "type": "Single Choice", "text": "...", "i18n": {"ar": "..."},
   "points": 1.0, "options": [{"key": "a", "text": "...", "i18n": {"ar": "..."}, "correct": 1}],
   "accepted": ["answer one", "answer two"], "tolerance": 0.0, "max_words": 0, "rubric": "..."}
"""
import hashlib
import random
import re
import unicodedata

AUTO_TYPES = {"Single Choice", "Multiple Choice", "True or False", "Short Answer", "Number"}
MANUAL_TYPES = {"Essay", "File Upload"}
ALL_TYPES = AUTO_TYPES | MANUAL_TYPES


def rng(seed: str) -> random.Random:
    return random.Random(int(hashlib.sha256(seed.encode()).hexdigest()[:16], 16))


def build_attempt(sections: list, bank: dict, seed: str, shuffle_questions: bool, shuffle_options: bool) -> list:
    """Freezes the sections and questions of one attempt.

    sections: [{"title", "instructions", "i18n": {lang: {"title", "instructions"}}, "minutes", "pass_mark", "items": [...]}].
      items: {"question": name} (fixed) or {"category", "difficulty", "job_family", "count"} (random draw),
             optionally {"points": override}.
    bank: {name: question dict} of active questions (fixed questions are always included).
    A question is never used twice in one attempt (across sections). The same seed gives the same attempt.
    Returns [{"key": "s1", ..., "questions": [{"key": "q1", ...}]}]; question keys run across sections.
    """
    r = rng(seed)
    used, out, n = set(), [], 0
    for si, sec in enumerate(sections, 1):
        picked = []
        for item in sec.get("items") or []:
            if item.get("question"):
                q = bank.get(item["question"])
                if q and q["question"] not in used:
                    used.add(q["question"])
                    picked.append(dict(q, points=float(item.get("points") or q.get("points") or 1)))
                continue
            pool = [q for name, q in sorted(bank.items()) if name not in used
                    and (not item.get("category") or q.get("category") == item["category"])
                    and (not item.get("difficulty") or q.get("difficulty") == item["difficulty"])
                    and (not item.get("job_family") or q.get("job_family") == item["job_family"])]
            for q in r.sample(pool, min(int(item.get("count") or 0), len(pool))):
                used.add(q["question"])
                picked.append(dict(q, points=float(item.get("points") or q.get("points") or 1)))
        if shuffle_questions:
            r.shuffle(picked)
        frozen = []
        for q in picked:
            n += 1
            q = dict(q, key=f"q{n}", options=[dict(o) for o in q.get("options") or []])
            for j, o in enumerate(q["options"]):
                o.setdefault("key", chr(97 + j))
            if shuffle_options and q["type"] in ("Single Choice", "Multiple Choice"):
                r.shuffle(q["options"])
            frozen.append(q)
        out.append({"key": f"s{si}", "title": sec.get("title") or "", "instructions": sec.get("instructions") or "",
                    "i18n": sec.get("i18n") or {},
                    "minutes": int(sec.get("minutes") or 0), "pass_mark": float(sec.get("pass_mark") or 0),
                    "questions": frozen})
    return [sec for sec in out if sec["questions"]]


def all_questions(sections: list) -> list:
    return [q for sec in sections for q in sec["questions"]]


def section_scores(sections: list, per_question: dict) -> list:
    """[{key, title, earned, total, percent, passed}] (passed is None when the section has no pass mark)."""
    out = []
    for sec in sections:
        total = sum(float(q.get("points") or 1) for q in sec["questions"])
        earned = sum(per_question.get(q["key"], 0.0) for q in sec["questions"])
        pct = round(earned / total * 100, 1) if total else 0.0
        out.append({"key": sec["key"], "title": sec.get("title", ""), "earned": round(earned, 2), "total": round(total, 2),
                    "percent": pct, "passed": (pct >= sec["pass_mark"]) if sec.get("pass_mark") else None})
    return out


def section_deadline(started_ts: float, minutes: int, extra_time_percent: int = 0) -> float | None:
    """Epoch seconds when the section closes, or None when it is untimed."""
    secs = attempt_seconds(minutes, extra_time_percent)
    return started_ts + secs if secs else None


def passed_overall(percent: float, pass_mark: float, sections: list) -> bool:
    """Passes when the overall mark is met and every section with its own pass mark is met."""
    return percent >= (pass_mark or 0) and all(s["passed"] is not False for s in sections)


def localize(base: str, i18n: dict | None, lang: str) -> str:
    """Approved translation for lang, else the original text."""
    return ((i18n or {}).get(lang) or "").strip() or (base or "")


def section_text(sec: dict, lang: str) -> tuple:
    tr = (sec.get("i18n") or {}).get(lang) or {}
    return (tr.get("title") or "").strip() or sec.get("title", ""), (tr.get("instructions") or "").strip() or sec.get("instructions", "")


def public_question(q: dict, lang: str) -> dict:
    """What the candidate's browser receives: never correct answers, accepted answers or rubrics."""
    return {"key": q["key"], "type": q["type"], "text": localize(q.get("text"), q.get("i18n"), lang), "points": q.get("points", 1),
            "max_words": int(q.get("max_words") or 0),
            "options": [{"key": o["key"], "text": localize(o.get("text"), o.get("i18n"), lang)} for o in q.get("options") or []]}


def option_translations(options: list, translated_lines: str) -> list:
    """Splits a translation's option lines (one per line, same order as the options) into per-option text."""
    lines = [l.strip() for l in (translated_lines or "").splitlines()]
    return [(lines[i] if i < len(lines) else "") for i in range(len(options))]


def _norm(s) -> str:
    s = unicodedata.normalize("NFKC", str(s or "")).strip().lower()
    s = re.sub(r"[ً-ْـ]", "", s)          # Arabic diacritics and tatweel
    s = re.sub(r"[أإآ]", "ا", s).replace("ة", "ه").replace("ى", "ي")
    return re.sub(r"\s+", " ", re.sub(r"[^\w\s.-]", "", s))


def score_question(q: dict, answer) -> tuple:
    """Returns (points, pending). pending=True means a person has to mark it."""
    pts, t = float(q.get("points") or 1), q["type"]
    if t in MANUAL_TYPES:
        return 0.0, bool(answer)                       # unanswered essays score 0 without marking
    if answer in (None, "", []):
        return 0.0, False
    if t in ("Single Choice", "True or False"):
        good = {o["key"] for o in q.get("options") or [] if o.get("correct")}
        return (pts if answer in good else 0.0), False
    if t == "Multiple Choice":
        good = {o["key"] for o in q.get("options") or [] if o.get("correct")}
        chosen = set(answer if isinstance(answer, list) else [answer])
        if not good:
            return 0.0, False
        right, wrong = len(chosen & good), len(chosen - good)
        return round(max(0.0, (right - wrong) / len(good)) * pts, 2), False
    if t == "Short Answer":
        return (pts if _norm(answer) in {_norm(a) for a in q.get("accepted") or [] if str(a).strip()} else 0.0), False
    if t == "Number":
        try:
            given = float(str(answer).replace(",", "").strip())
            target = float(str((q.get("accepted") or ["nan"])[0]).replace(",", ""))
        except ValueError:
            return 0.0, False
        return (pts if abs(given - target) <= float(q.get("tolerance") or 0) else 0.0), False
    return 0.0, False


def score_attempt(questions: list, answers: dict, manual: dict | None = None) -> dict:
    """manual: {question key: points} from markers (overrides automatic marks too)."""
    manual = manual or {}
    total = earned = 0.0
    pending = []
    per = {}
    for q in questions:
        pts = float(q.get("points") or 1)
        total += pts
        if q["key"] in manual and manual[q["key"]] is not None:
            got = max(0.0, min(pts, float(manual[q["key"]])))
            wait = False
        else:
            got, wait = score_question(q, answers.get(q["key"]))
        if wait:
            pending.append(q["key"])
        per[q["key"]] = got
        earned += got
    pct = round(earned / total * 100, 1) if total else 0.0
    return {"earned": round(earned, 2), "total": round(total, 2), "percent": pct, "pending": pending, "per_question": per}


def word_count(text: str) -> int:
    return len(re.findall(r"\S+", text or ""))


def attempt_seconds(duration_minutes: int, extra_time_percent: int = 0) -> int:
    """0 means untimed."""
    if not duration_minutes:
        return 0
    return int(round(duration_minutes * 60 * (1 + max(0, extra_time_percent) / 100)))


def can_start(status: str, attempts_used: int, attempts_allowed: int, deadline_passed: bool) -> str:
    """'' if a new attempt may start, otherwise a reason code."""
    if status in ("Cancelled",):
        return "assessment_cancelled"
    if deadline_passed:
        return "assessment_expired"
    if attempts_used >= attempts_allowed:
        return "no_attempts_left"
    return ""


def pick_result(scores: list, policy: str) -> float | None:
    """Score that counts for the candidate: 'Highest' or 'Latest' of the marked attempts (oldest first)."""
    scores = [s for s in scores if s is not None]
    if not scores:
        return None
    return max(scores) if policy == "Highest" else scores[-1]


def clean_answer(q: dict, value):
    """Normalises what the browser sent for a question; raises ValueError for invalid answers."""
    t = q["type"]
    keys = {o["key"] for o in q.get("options") or []}
    if value in (None, "", []):
        return None
    if t in ("Single Choice", "True or False"):
        if value not in keys:
            raise ValueError("invalid_answer")
        return value
    if t == "Multiple Choice":
        vals = value if isinstance(value, list) else [value]
        if not all(v in keys for v in vals):
            raise ValueError("invalid_answer")
        return sorted(set(vals))
    if t == "Number":
        s = str(value).strip()[:40]
        float(s.replace(",", ""))
        return s
    if t in ("Short Answer", "Essay"):
        s = str(value)[: 500 if t == "Short Answer" else 30000]
        return s
    if t == "File Upload":
        if not isinstance(value, dict) or not value.get("file_url"):
            raise ValueError("invalid_answer")
        return {"file_url": str(value["file_url"])[:300], "name": str(value.get("name", ""))[:200]}
    raise ValueError("invalid_answer")
