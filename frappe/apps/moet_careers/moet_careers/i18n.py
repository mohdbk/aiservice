"""Portal languages, portal wording and job translations.

Wording: English and Arabic ship with the app in portal_text/<lang>.json. HR can override any key or add a
language through Portal Text rows; only Approved rows are used. Job titles and descriptions are translated
per job in the "Portal Translations" table. The language model can suggest drafts for both, but nothing
reaches the portal until HR approves it (editing a draft approves it).
"""
import json
import pathlib

import frappe
from frappe.utils.html_utils import clean_html

from moet_careers.portal_utils import content_hash, is_rtl, merge_text, valid_lang

SETTINGS = "MoET Careers Settings"
CATALOG_DIR = pathlib.Path(__file__).parent / "portal_text"
EDITORS = ("System Manager", "HR Manager")


# ---------------- languages ----------------
def portal_languages() -> tuple[list[dict], str]:
    """Enabled portal languages (in settings order) and the default language."""
    s = frappe.get_cached_doc(SETTINGS)
    codes = [r.language for r in (s.get("languages") or []) if r.enabled and valid_lang(r.language)] or ["ar", "en"]
    default = s.get("default_language") or codes[0]
    if default not in codes:
        codes.insert(0, default)
    langs = [{"code": c, "label": language_label(c), "dir": "rtl" if is_rtl(c) else "ltr"} for c in codes]
    return langs, default


def language_label(code: str) -> str:
    names = {"ar": "العربية", "en": "English"}
    return names.get(code) or frappe.get_cached_value("Language", code, "language_name") or code


def language_name_en(code: str) -> str:
    names = {"ar": "Arabic", "en": "English", "fr": "French", "ur": "Urdu", "hi": "Hindi", "zh": "Chinese",
             "ru": "Russian", "es": "Spanish", "de": "German", "fa": "Persian", "tl": "Filipino", "bn": "Bengali",
             "ml": "Malayalam", "ta": "Tamil", "it": "Italian", "pt": "Portuguese", "tr": "Turkish", "ja": "Japanese",
             "ko": "Korean", "id": "Indonesian"}
    return names.get(code.split("-")[0], language_label(code))


# ---------------- portal wording ----------------
def _catalog(lang: str) -> dict:
    p = CATALOG_DIR / f"{lang}.json"
    return json.loads(p.read_text(encoding="utf-8")) if p.exists() else {}


def base_text(lang: str) -> dict:
    """Shipped wording for a language (English base merged with the language file)."""
    return merge_text(_catalog("en"), _catalog(lang)) if lang != "en" else _catalog("en")


def approved_overrides(lang: str) -> dict:
    key = f"moet_portal_text::{lang}"
    cached = frappe.cache.get_value(key)
    if cached is None:
        rows = frappe.get_all("Portal Text", filters={"language": lang, "status": "Approved"}, fields=["key", "text"])
        cached = {r.key: r.text for r in rows}
        frappe.cache.set_value(key, cached, expires_in_sec=3600)
    return cached


def portal_text(lang: str) -> dict:
    return merge_text(_catalog("en"), _catalog(lang), approved_overrides(lang))


@frappe.whitelist(methods=["POST"])
def suggest_portal_text(language: str):
    frappe.only_for(EDITORS)
    if not valid_lang(language) or not frappe.db.exists("Language", language):
        frappe.throw("Choose a valid language.")
    frappe.enqueue("moet_careers.i18n._suggest_portal_text_job", queue="long", timeout=1800, language=language,
                   user=frappe.session.user, job_id=f"moet-portal-text-{language}", deduplicate=True)
    return ("The language model is drafting the portal text. In a few minutes, review the drafts in "
            f"<a href='/app/portal-text?language={language}&status=Suggested'>Portal Text</a> and set them to Approved.")


def _suggest_portal_text_job(language: str, user: str | None = None):
    base = _catalog("en")
    shipped = _catalog(language)
    existing = set(frappe.get_all("Portal Text", filters={"language": language}, pluck="key"))
    todo = [k for k in base if k not in existing and k not in shipped]
    added = 0
    for i in range(0, len(todo), 40):
        batch = todo[i:i + 40]
        out = _llm_json(
            "You translate the user interface of a UAE government careers website. Translate each English string "
            f"into {language_name_en(language)} ({language}). Use a formal, clear and friendly government tone, short "
            "like a website label, and consistent terms. Keep placeholders in curly braces such as {count} exactly "
            "as they are. Keep brand names. Never use em dashes. Return every id.",
            json.dumps([{"id": n, "text": base[k]} for n, k in enumerate(batch)], ensure_ascii=False),
            {"type": "object", "additionalProperties": False, "required": ["items"],
             "properties": {"items": {"type": "array", "items": {
                 "type": "object", "additionalProperties": False, "required": ["id", "text"],
                 "properties": {"id": {"type": "integer"}, "text": {"type": "string"}}}}}},
            "portal_text")
        by_id = {it.get("id"): (it.get("text") or "").strip() for it in out.get("items") or []}
        for n, k in enumerate(batch):
            text = by_id.get(n)
            if not text:
                continue
            frappe.get_doc({"doctype": "Portal Text", "language": language, "key": k, "status": "Suggested",
                            "text": text, "suggested_hash": content_hash(text)}).insert(ignore_permissions=True)
            added += 1
        frappe.db.commit()
    if user:
        frappe.publish_realtime("msgprint", f"{added} portal text drafts for {language} are ready for review.", user=user)
    return added


# ---------------- job translations ----------------
def source_language(title: str, description: str) -> str:
    from ai_recruitment.utils import arabic_ratio, strip_html

    return "ar" if arabic_ratio(f"{title} {strip_html(description or '')}") > 0.3 else "en"


def approve_rows(rows, fields: tuple):
    """Shared approval rule for translation rows: editing a suggestion approves it; record who approved."""
    for row in rows or []:
        if row.status == "Suggested" and row.suggested_hash and \
                content_hash(*[row.get(f) for f in fields]) != row.suggested_hash:
            row.status = "Approved"
        if row.status == "Approved" and not row.approved_by:
            row.approved_by = frappe.session.user
        if row.status != "Approved":
            row.approved_by = None


def on_job_validate(doc, method=None):
    approve_rows(doc.get("portal_translations"), ("job_title", "description"))


@frappe.whitelist(methods=["POST"])
def suggest_job_translations(job: str):
    doc = frappe.get_doc("Job Opening", job)
    doc.check_permission("write")
    missing = _missing_languages(doc)
    if not missing:
        return "Every portal language already has a translation row. Delete a row to get a new suggestion."
    frappe.enqueue("moet_careers.i18n._suggest_job_job", queue="long", timeout=900, job=job, user=frappe.session.user,
                   job_id=f"moet-job-translate-{job}", deduplicate=True)
    return f"Drafting {', '.join(missing)}. The form refreshes when the suggestions are ready; review and approve them."


def _missing_languages(doc) -> list[str]:
    langs, _ = portal_languages()
    src = source_language(doc.job_title or "", doc.description or "")
    have = {r.language for r in doc.get("portal_translations") or []}
    return [l["code"] for l in langs if l["code"] != src and l["code"] not in have]


def _suggest_job_job(job: str, user: str | None = None):
    doc = frappe.get_doc("Job Opening", job)
    src = source_language(doc.job_title or "", doc.description or "")
    idx = len(doc.get("portal_translations") or [])
    done = []
    for lang in _missing_languages(doc):
        out = _llm_json(
            "You translate job advertisements for a UAE government ministry. Translate the job title and the HTML "
            f"description from {language_name_en(src)} into {language_name_en(lang)} ({lang}). Keep every HTML tag "
            "and the structure, keep numbers, dates, product names and acronyms, use formal government style, and "
            "never use em dashes. Return the translated title and HTML only.",
            json.dumps({"job_title": doc.job_title or "", "description_html": doc.description or ""}, ensure_ascii=False),
            {"type": "object", "additionalProperties": False, "required": ["job_title", "description_html"],
             "properties": {"job_title": {"type": "string"}, "description_html": {"type": "string"}}},
            "job_translation")
        title = (out.get("job_title") or "").strip()[:140]
        desc = clean_html(out.get("description_html") or "")
        if not title:
            continue
        idx += 1
        frappe.get_doc({"doctype": "Job Opening Translation", "parent": job, "parenttype": "Job Opening",
                        "parentfield": "portal_translations", "idx": idx, "language": lang, "status": "Suggested",
                        "job_title": title, "description": desc,
                        "suggested_hash": content_hash(title, desc)}).db_insert()
        done.append(lang)
    frappe.db.commit()
    if done:
        frappe.get_doc("Job Opening", job).notify_update()
    if user:
        msg = f"Translation suggestions ready for {', '.join(done)}. Review and approve them." if done else \
            "No translation suggestions were added."
        frappe.publish_realtime("moet_translations_ready", {"job": job, "message": msg}, user=user)
    return done


# ---------------- assessment translations ----------------
@frappe.whitelist(methods=["POST"])
def suggest_assessment_translations(doctype: str, name: str):
    if doctype not in ("Assessment Question", "Assessment", "Screening Question"):
        frappe.throw("Not supported.")
    doc = frappe.get_doc(doctype, name)
    doc.check_permission("write")
    frappe.enqueue("moet_careers.i18n._suggest_assessment_job", queue="long", timeout=1800, doctype=doctype, name=name,
                   user=frappe.session.user, job_id=f"moet-asm-translate-{name}", deduplicate=True)
    return "Drafting translations for every portal language that has none yet. The form refreshes when they are ready."


def _suggest_assessment_job(doctype: str, name: str, user: str | None = None):
    from frappe.utils import strip_html_tags

    doc = frappe.get_doc(doctype, name)
    langs, _ = portal_languages()
    if doctype == "Screening Question":
        src = source_language(doc.text or "", "")
        have = {t.language for t in doc.get("translations") or []}
        opts = [o for o in (doc.options or "").splitlines() if o.strip()]
        payload = {"question": doc.text or "", "options": opts}
        schema = {"type": "object", "additionalProperties": False, "required": ["question", "options"],
                  "properties": {"question": {"type": "string"}, "options": {"type": "array", "items": {"type": "string"}}}}
    elif doctype == "Assessment Question":
        src = source_language(strip_html_tags(doc.text or ""), "")
        have = {t.language for t in doc.get("translations") or []}
        payload = {"question_html": doc.text or "", "options": [o.option for o in doc.options]}
        schema = {"type": "object", "additionalProperties": False, "required": ["question_html", "options"],
                  "properties": {"question_html": {"type": "string"}, "options": {"type": "array", "items": {"type": "string"}}}}
    else:
        src = source_language(doc.title or "", doc.instructions or "")
        have = {t.language for t in doc.get("translations") or []}
        payload = {"title": doc.title or "", "instructions_html": doc.instructions or "",
                   "sections": [{"title": s.section_title or "", "instructions": s.instructions or ""} for s in doc.sections]}
        schema = {"type": "object", "additionalProperties": False, "required": ["title", "instructions_html", "sections"],
                  "properties": {"title": {"type": "string"}, "instructions_html": {"type": "string"},
                                 "sections": {"type": "array", "items": {"type": "object", "additionalProperties": False,
                                              "required": ["title", "instructions"],
                                              "properties": {"title": {"type": "string"}, "instructions": {"type": "string"}}}}}}
    done = []
    for lang in [l["code"] for l in langs if l["code"] != src and l["code"] not in have]:
        out = _llm_json(
            "You translate recruitment test content for a UAE government ministry. Translate from "
            f"{language_name_en(src)} into {language_name_en(lang)} ({lang}). Keep HTML tags, numbers, names, formulas, "
            "code and technical terms; keep the same number and order of options and sections. Do not change the "
            "meaning or make any option more or less obviously correct. Never use em dashes.",
            json.dumps(payload, ensure_ascii=False), schema, "assessment_translation")
        if doctype == "Screening Question":
            text = (out.get("question") or "").strip()[:1000]
            opts_t = "\n".join((o or "").strip() for o in (out.get("options") or [])[:len(opts)])
            doc.append("translations", {"language": lang, "status": "Suggested", "text": text, "options": opts_t,
                                        "suggested_hash": content_hash(text, opts_t)})
        elif doctype == "Assessment Question":
            text = clean_html(out.get("question_html") or "")
            opts = "\n".join((o or "").strip() for o in (out.get("options") or [])[:len(doc.options)])
            doc.append("translations", {"language": lang, "status": "Suggested", "text": text, "options": opts,
                                        "suggested_hash": content_hash(text, opts)})
        else:
            title, instr = (out.get("title") or "").strip()[:140], clean_html(out.get("instructions_html") or "")
            doc.append("translations", {"language": lang, "status": "Suggested", "title": title, "instructions": instr,
                                        "suggested_hash": content_hash(title, instr)})
            for i, sec in enumerate((out.get("sections") or [])[:len(doc.sections)], 1):
                st, si = (sec.get("title") or "").strip()[:140], (sec.get("instructions") or "").strip()
                doc.append("section_translations", {"language": lang, "section": i, "status": "Suggested", "title": st,
                                                    "instructions": si, "suggested_hash": content_hash(st, si)})
        done.append(lang)
    if done:
        doc.flags.ignore_permissions = True
        doc.save()
        doc.notify_update()
    if user:
        frappe.publish_realtime("moet_translations_ready", {"job": name, "message":
                                f"Translation suggestions ready for {', '.join(done)}. Review and approve them." if done
                                else "No translation suggestions were added."}, user=user)
    return done


# ---------------- language model ----------------
def _llm_json(system: str, user: str, schema: dict, name: str) -> dict:
    from ai_recruitment import llm
    from ai_recruitment.utils import clean, extract_json

    msg = llm.chat_completion([{"role": "system", "content": system}, {"role": "user", "content": user}],
                              schema=schema, schema_name=name, temperature=0.1, timeout=300)
    return clean(extract_json(msg.get("content") or "")) or {}
