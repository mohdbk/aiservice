"""Arabic and English side by side in Desk.

Arabic is the original: HR writes it in the main fields and the English in the "(English)" fields next to them.
On save, the English fields are copied into the entity's translation table as its "en" row, so storage stays
multilingual: more languages can be added later through the same tables (hidden in Desk for now) without
changing the portal.
"""
import re

import frappe

from moet_careers.portal_utils import review_status, content_hash

LANG = "en"
FLAG = "english_approved"
OLD = "ar"  # translations saved when English was the original

# doctype: translation table, {translation field: Arabic field}, option rows (table, Arabic field) and
# section rows (table, {translation field: Arabic field}) where the entity has them.
SPECS = {
    "Job Opening": {"table": "portal_translations", "pairs": {"job_title": "job_title_en", "description": "description_en"}},
    "Screening Question": {"table": "translations", "pairs": {"text": "text_en", "options": "options_en"}},
    "Assessment Question": {"table": "translations", "pairs": {"text": "text_en"}, "option_rows": ("options", "option_en")},
    "Assessment": {"table": "translations", "pairs": {"title": "title_en", "instructions": "instructions_en"},
                   "sections": ("sections", "section_translations",
                                {"title": "section_title_en", "instructions": "instructions_en"})},
}


def _filled(v) -> bool:
    return bool(re.sub(r"<[^>]+>|&nbsp;|\s", "", str(v or "")))


def _values(doc, spec) -> tuple[dict, list]:
    vals = {tf: (doc.get(af) or "").strip() if _filled(doc.get(af)) else "" for tf, af in spec["pairs"].items()}
    if spec.get("option_rows"):
        table, af = spec["option_rows"]
        lines = [(r.get(af) or "").strip().replace("\n", " ") for r in doc.get(table) or []]
        vals["options"] = "\n".join(lines) if any(lines) else ""
    if doc.doctype == "Screening Question" and vals.get("options"):
        vals["options"] = "\n".join(l.strip() for l in vals["options"].splitlines() if l.strip())
    sections = []
    if spec.get("sections"):
        table, _, pairs = spec["sections"]
        for i, row in enumerate(doc.get(table) or [], 1):
            sv = {tf: (row.get(af) or "").strip() for tf, af in pairs.items()}
            sections.append((i, sv))
    return vals, sections


def sync(doc, method=None):
    """Copies the English fields into the "en" translation row(s) and sets the approval status."""
    spec = SPECS.get(doc.doctype)
    if not spec:
        return
    vals, sections = _values(doc, spec)
    has_main = any(_filled(v) for v in vals.values())
    has_sec = any(_filled(v) for _, sv in sections for v in sv.values())
    rows = [r for r in doc.get(spec["table"]) or [] if r.language == LANG]
    sec_table = spec["sections"][1] if spec.get("sections") else None
    if not (has_main or has_sec):
        doc.set(spec["table"], [r for r in doc.get(spec["table"]) or [] if r.language != LANG])
        if sec_table:
            doc.set(sec_table, [r for r in doc.get(sec_table) or [] if r.language != LANG])
        doc.set(FLAG, 0)
        return
    row = rows[0] if rows else doc.append(spec["table"], {"language": LANG})
    if len(rows) > 1:
        doc.set(spec["table"], [r for r in doc.get(spec["table"]) if r.language != LANG or r is row])
    row.update(vals)
    current = content_hash(*vals.values(), *[v for _, sv in sections for v in sv.values()])
    before = None if doc.is_new() else doc.get_doc_before_save()
    status, keep = review_status(bool(doc.get(FLAG)), bool(before and before.get(FLAG)),
                                 bool(doc.flags.moet_suggested), row.get("suggested_hash"), current)
    row.status, row.suggested_hash = status, keep
    doc.set(FLAG, 1 if status == "Approved" else 0)
    if sec_table:
        keep_rows = [r for r in doc.get(sec_table) or [] if r.language != LANG]
        doc.set(sec_table, keep_rows)
        for i, sv in sections:
            if any(_filled(v) for v in sv.values()):
                doc.append(sec_table, {"language": LANG, "section": i, "status": status, **sv})


def backfill():
    """One time: makes Arabic the original of content saved while English was.

    Where the main fields are English and an Arabic translation exists, the Arabic moves into the main fields and
    the English into the "(English)" fields, keeping its approval. The old Arabic row is then dropped.
    """
    from moet_careers.i18n import source_language

    if frappe.db.get_default("moet_careers_arabic_original"):
        return
    for doctype, spec in SPECS.items():
        if not frappe.db.exists("DocType", doctype):
            continue
        parents = frappe.get_all(_child(doctype, spec["table"]), filters={"language": OLD, "parenttype": doctype},
                                 pluck="parent", distinct=True)
        for name in parents:
            try:
                _swap(frappe.get_doc(doctype, name), spec, source_language)
            except Exception:
                frappe.log_error(frappe.get_traceback(), f"Arabic original: {doctype} {name}")
    frappe.db.set_default("moet_careers_arabic_original", "1")


def _swap(doc, spec, source_language):
    row = next((r for r in doc.get(spec["table"]) if r.language == OLD), None)
    pairs = spec["pairs"]
    main_text = " ".join(re.sub(r"<[^>]+>", " ", str(doc.get(f) or "")) for f in pairs)
    if not row or source_language(main_text, "") != "en":
        return
    db = frappe.db.set_value
    upd = {}
    for tf, ef in pairs.items():
        upd[ef] = doc.get(tf) or ""
        if (row.get(tf) or "").strip():
            upd[tf] = row.get(tf)
    upd[FLAG] = 1
    if spec.get("option_rows"):
        table, ef = spec["option_rows"]
        lines = (row.get("options") or "").splitlines()
        for i, opt in enumerate(doc.get(table)):
            ou = {ef: opt.option}
            if i < len(lines) and lines[i].strip():
                ou["option"] = lines[i].strip()
            db(opt.doctype, opt.name, ou, update_modified=False)
        upd.pop("options", None)
    if spec.get("sections"):
        table, sec_table, spairs = spec["sections"]
        tr = {st.section: st for st in doc.get(sec_table) or [] if st.language == OLD}
        base_fields = {"title": "section_title", "instructions": "instructions"}
        for i, sec in enumerate(doc.get(table), 1):
            su = {ef: sec.get(base_fields[tf]) or "" for tf, ef in spairs.items()}
            st = tr.get(i)
            for tf in spairs:
                if st and (st.get(tf) or "").strip():
                    su[base_fields[tf]] = st.get(tf)
            db(sec.doctype, sec.name, su, update_modified=False)
        frappe.db.delete(_child(doc.doctype, sec_table), {"parent": doc.name, "language": OLD})
    db(doc.doctype, doc.name, upd, update_modified=False)
    frappe.db.delete(_child(doc.doctype, spec["table"]), {"parent": doc.name, "language": OLD})
    # Save once so the English row is written and the title fields follow the Arabic.
    doc = frappe.get_doc(doc.doctype, doc.name)
    sync(doc)
    for r in doc.get(spec["table"]):
        if r.language == LANG:
            r.status = "Approved"
    if spec.get("sections"):
        for r in doc.get(spec["sections"][1]):
            if r.language == LANG:
                r.status = "Approved"
    doc.flags.ignore_validate = doc.flags.ignore_permissions = doc.flags.ignore_mandatory = True
    if doc.doctype == "Assessment Question":
        from frappe.utils import strip_html_tags
        doc.title = strip_html_tags(doc.text or "").strip()[:120] or doc.name
    if doc.doctype == "Screening Question":
        doc.title = (doc.text or "").strip()[:120]
    doc.save()


def _child(doctype: str, fieldname: str) -> str:
    return frappe.get_meta(doctype).get_field(fieldname).options
