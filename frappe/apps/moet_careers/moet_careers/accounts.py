"""Applicant account links (email activation, password reset) and their emails.

Links are random 256-bit tokens. Only their SHA-256 hash is stored (Portal Account Token), each works once,
activation links last 48 hours and reset links one hour. Emails use the portal wording (Portal Text), so HR
can edit them, in the applicant's portal language.
"""
import hashlib
import secrets

import frappe
from frappe.utils import add_to_date, escape_html, get_datetime, now_datetime

SETTINGS = "MoET Careers Settings"
TOKEN = "Portal Account Token"
LIFETIME = {"Verify Email": {"hours": 48}, "Reset Password": {"hours": 1}}
PAGE = {"Verify Email": "verify-email", "Reset Password": "reset-password"}
MAIL = {"Verify Email": ("email.verify_subject", "email.verify_body"), "Reset Password": ("email.reset_subject", "email.reset_body")}


def mail_configured() -> bool:
    return bool(frappe.db.exists("Email Account", {"enable_outgoing": 1}))


def verification_required() -> bool:
    return bool(frappe.get_cached_doc(SETTINGS).require_email_verification) and mail_configured()


def safe_next(path: str | None) -> str:
    path = (path or "").strip()
    return path if path.startswith("/") and not path.startswith("//") and "\\" not in path and len(path) < 300 else ""


def _hash(token: str) -> str:
    return hashlib.sha256(token.encode()).hexdigest()


def issue(user: str, purpose: str, lang: str, next_path: str = "") -> str:
    """Creates a new link for the user (older unused links for the same purpose stop working)."""
    frappe.db.sql(f"update `tab{TOKEN}` set used=1 where user=%s and purpose=%s and used=0", (user, purpose))
    token = secrets.token_urlsafe(32)
    frappe.get_doc({"doctype": TOKEN, "user": user, "purpose": purpose, "token_hash": _hash(token),
                    "expires_at": add_to_date(now_datetime(), **LIFETIME[purpose]), "lang": lang,
                    "next_path": safe_next(next_path)}).insert(ignore_permissions=True)
    return token


def consume(token: str, purpose: str):
    """Returns the token row for a valid link and marks it used; raises ValueError('invalid'|'expired')."""
    if not token or len(token) > 200:
        raise ValueError("invalid")
    row = frappe.db.get_value(TOKEN, {"token_hash": _hash(token), "purpose": purpose},
                              ["name", "user", "used", "expires_at", "next_path", "lang"], as_dict=True)
    if not row or row.used:
        raise ValueError("invalid")
    if get_datetime(row.expires_at) < now_datetime():
        raise ValueError("expired")
    frappe.db.set_value(TOKEN, row.name, "used", 1, update_modified=False)
    return row


def portal_lang(lang: str) -> str:
    from moet_careers.i18n import portal_languages

    langs, default = portal_languages()
    return lang if lang in [l["code"] for l in langs] else default


def send_portal_email(user: str, lang: str, subject_key: str, body_key: str, link: str, params: dict | None = None) -> bool:
    """Queues a bilingual-ready portal email built from Portal Text keys. {name}, {link} and params are filled in."""
    from moet_careers.i18n import portal_text

    if not mail_configured():
        frappe.log_error(f"Careers portal email not sent ({subject_key}): no outgoing Email Account", "Careers portal email")
        return False
    text = portal_text(lang)
    values = {"name": frappe.utils.get_fullname(user) or user, "link": link, **(params or {})}
    body, subject = text[body_key], text[subject_key]
    for k, v in values.items():
        body, subject = body.replace("{" + k + "}", str(v)), subject.replace("{" + k + "}", str(v))
    rtl = lang.split("-")[0] in ("ar", "fa", "ur", "he")
    parts = []
    for para in body.split("\n\n"):
        if link and para.strip() == link:
            parts.append(f'<p style="margin:0 0 16px"><a href="{link}" style="display:inline-block;background:#92722a;color:#ffffff;'
                         f'padding:12px 20px;border-radius:8px;text-decoration:none;font-weight:bold">{escape_html(subject)}</a></p>'
                         f'<p style="margin:0 0 16px;font-size:12px;color:#555;word-break:break-all">{escape_html(link)}</p>')
        else:
            parts.append(f'<p style="margin:0 0 16px">{escape_html(para).replace(chr(10), "<br>")}</p>')
    html = (f'<div dir="{"rtl" if rtl else "ltr"}" style="font-family:Arial,sans-serif;font-size:15px;line-height:1.6;'
            f'color:#232528;max-width:560px">{"".join(parts)}<p style="margin:24px 0 0;color:#555">'
            f'{escape_html(text["email.signature"])}</p></div>')
    frappe.sendmail(recipients=[user], subject=subject, message=html, delayed=True, reference_doctype="User", reference_name=user)
    return True


def portal_link(lang: str, path: str) -> str:
    base = (frappe.get_cached_doc(SETTINGS).portal_url or "").rstrip("/")
    return f"{base}/{lang}{path}" if base else ""


def send(user: str, purpose: str, lang: str, next_path: str = "") -> bool:
    """Issues an account link and queues the email. Returns False when no portal address or mail account is set."""
    lang = portal_lang(lang)
    if not portal_link(lang, "") or not mail_configured():
        frappe.log_error(f"Careers portal email not sent ({purpose}): set Portal Address and an outgoing Email Account",
                         "Careers portal email")
        return False
    token = issue(user, purpose, lang, next_path)
    subject_key, body_key = MAIL[purpose]
    return send_portal_email(user, lang, subject_key, body_key, portal_link(lang, f"/{PAGE[purpose]}?token={token}"))


def purge_old_tokens():
    frappe.db.sql(f"delete from `tab{TOKEN}` where expires_at < %s", (add_to_date(now_datetime(), days=-7),))
