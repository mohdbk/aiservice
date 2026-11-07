// AI Recruitment Assistant — Desk chat panel (Ministry of Economy & Tourism)
// Calls whitelisted methods with the user's own Frappe session; no tokens in the page.
(function () {
  "use strict";

  // ---- "Re-score with AI" on the Job Applicant form ----
  frappe.ui.form.on("Job Applicant", {
    refresh(frm) {
      if (frm.is_new() || !(frappe.boot.ai_recruitment || {}).chat) return;
      frm.add_custom_button(__("Re-score with AI"), async () => {
        const msg = await frappe.xcall("ai_recruitment.api.rescore", { applicant: frm.doc.name });
        frappe.show_alert({ message: msg, indicator: "blue" });
      });
    },
  });

  function init() {
    if (window.__aiRecruitChat || !(frappe.boot.ai_recruitment || {}).chat) return;
    window.__aiRecruitChat = true;

    const KEY = "ai_recruitment_chat_v2::" + frappe.session.user;
    let history = [];
    try { history = JSON.parse(sessionStorage.getItem(KEY) || "[]"); } catch (e) { history = []; }
    const save = () => { try { sessionStorage.setItem(KEY, JSON.stringify(history.slice(-30))); } catch (e) {} };

    // ---- safe mini-markdown: escape first, then format; only http(s) and relative links ----
    const esc = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
    function inline(s) {
      return esc(s)
        .replace(/`([^`]+)`/g, "<code>$1</code>")
        .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
        .replace(/(^|[^*\w])\*([^*\n]+)\*(?!\w)/g, "$1<em>$2</em>")
        .replace(/\[([^\]]+)\]\((https?:\/\/[^\s)]+|\/[^\s)]*)\)/g,
          '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>');
    }
    function md(src) {
      const lines = String(src || "").replace(/\r/g, "").split("\n");
      let out = "", i = 0;
      while (i < lines.length) {
        const line = lines[i];
        if (/^```/.test(line)) {
          const buf = []; i++;
          while (i < lines.length && !/^```/.test(lines[i])) buf.push(lines[i++]);
          i++; out += "<pre><code>" + esc(buf.join("\n")) + "</code></pre>"; continue;
        }
        if (/^\s*\|.*\|\s*$/.test(line)) {
          const rows = [];
          while (i < lines.length && /^\s*\|.*\|\s*$/.test(lines[i])) rows.push(lines[i++]);
          const cells = (r) => r.trim().replace(/^\||\|$/g, "").split("|").map((c) => c.trim());
          const body = rows.filter((r) => !/^\s*\|?\s*:?-{2,}/.test(r));
          if (body.length) {
            out += '<div class="aic-table"><table><thead><tr>' + cells(body[0]).map((c) => "<th>" + inline(c) + "</th>").join("")
              + "</tr></thead><tbody>" + body.slice(1).map((r) => "<tr>" + cells(r).map((c) => "<td>" + inline(c) + "</td>").join("") + "</tr>").join("")
              + "</tbody></table></div>";
          }
          continue;
        }
        const h = line.match(/^(#{1,4})\s+(.*)$/);
        if (h) { out += "<div class=\"aic-h\">" + inline(h[2]) + "</div>"; i++; continue; }
        if (/^\s*([-*•])\s+/.test(line)) {
          out += "<ul>";
          while (i < lines.length && /^\s*([-*•])\s+/.test(lines[i])) out += "<li>" + inline(lines[i++].replace(/^\s*[-*•]\s+/, "")) + "</li>";
          out += "</ul>"; continue;
        }
        if (/^\s*\d+[.)]\s+/.test(line)) {
          out += "<ol>";
          while (i < lines.length && /^\s*\d+[.)]\s+/.test(lines[i])) out += "<li>" + inline(lines[i++].replace(/^\s*\d+[.)]\s+/, "")) + "</li>";
          out += "</ol>"; continue;
        }
        if (!line.trim()) { i++; continue; }
        const para = [];
        while (i < lines.length && lines[i].trim() && !/^(```|#{1,4}\s|\s*[-*•]\s|\s*\d+[.)]\s|\s*\|)/.test(lines[i])) para.push(inline(lines[i++]));
        out += "<p>" + para.join("<br>") + "</p>";
      }
      return out;
    }

    const style = document.createElement("style");
    style.textContent = `
      .aic-fab{position:fixed;right:20px;bottom:20px;z-index:1030;border:0;border-radius:999px;padding:10px 16px;
        font-weight:600;background:var(--primary,#2490ef);color:#fff;box-shadow:0 4px 14px rgba(0,0,0,.18);cursor:pointer}
      .aic-panel{position:fixed;right:20px;bottom:72px;z-index:1031;width:min(420px,calc(100vw - 24px));height:min(72vh,640px);
        display:none;flex-direction:column;background:var(--card-bg,#fff);color:var(--text-color,#1f272e);
        border:1px solid var(--border-color,#d1d8dd);border-radius:12px;box-shadow:0 10px 30px rgba(0,0,0,.2);overflow:hidden}
      .aic-panel.open{display:flex}
      @media (max-width:600px){.aic-panel{right:12px;bottom:68px}.aic-fab{right:12px;bottom:12px}}
      .aic-head{display:flex;justify-content:space-between;align-items:center;padding:10px 14px;font-weight:600;
        border-bottom:1px solid var(--border-color,#d1d8dd)}
      .aic-head button{border:0;background:none;color:inherit;cursor:pointer;font-size:13px;opacity:.75}
      .aic-head .aic-x{font-size:20px;line-height:1;opacity:1}
      .aic-log{flex:1;overflow-y:auto;padding:12px;display:flex;flex-direction:column;gap:8px}
      .aic-msg{max-width:90%;padding:8px 11px;border-radius:10px;word-wrap:break-word;font-size:13px;line-height:1.5}
      .aic-msg p{margin:0 0 6px}.aic-msg p:last-child{margin:0}
      .aic-msg ul,.aic-msg ol{margin:4px 0 6px;padding-inline-start:18px}
      .aic-msg code{background:rgba(0,0,0,.06);padding:1px 4px;border-radius:4px;font-size:12px}
      .aic-msg pre{background:rgba(0,0,0,.06);padding:8px;border-radius:6px;overflow-x:auto;margin:4px 0}
      .aic-msg pre code{background:none;padding:0}
      .aic-h{font-weight:700;margin:6px 0 4px}
      .aic-table{overflow-x:auto;margin:4px 0}
      .aic-table table{border-collapse:collapse;font-size:12px;width:100%}
      .aic-table th,.aic-table td{border:1px solid var(--border-color,#d1d8dd);padding:4px 6px;text-align:start;vertical-align:top}
      .aic-table th{background:rgba(0,0,0,.04)}
      .aic-user{align-self:flex-end;background:var(--primary,#2490ef);color:#fff;white-space:pre-wrap}
      .aic-bot{align-self:flex-start;background:var(--subtle-fg,#f4f5f6)}
      .aic-bot a{color:var(--primary,#2490ef);text-decoration:underline}
      .aic-approve{display:block;margin-top:8px;border:1px solid var(--primary,#2490ef);background:transparent;
        color:var(--primary,#2490ef);border-radius:8px;padding:6px 10px;font-size:12px;cursor:pointer;text-align:start;width:100%}
      .aic-approve:disabled{opacity:.75;cursor:default}
      .aic-form{display:flex;gap:8px;padding:10px;border-top:1px solid var(--border-color,#d1d8dd)}
      .aic-form textarea{flex:1;resize:none;border:1px solid var(--border-color,#d1d8dd);border-radius:8px;padding:8px;
        font-size:13px;background:var(--control-bg,#f4f5f6);color:inherit}
      .aic-form button{border:0;border-radius:8px;padding:0 14px;background:var(--primary,#2490ef);color:#fff;font-weight:600}
      .aic-note{font-size:11px;opacity:.65;padding:0 12px 8px}`;
    document.head.appendChild(style);

    const fab = document.createElement("button");
    fab.className = "aic-fab"; fab.textContent = __("Ask AI");
    const panel = document.createElement("div");
    panel.className = "aic-panel";
    panel.innerHTML =
      '<div class="aic-head"><span>' + esc(__("Recruitment Assistant")) + '</span><span>'
      + '<button class="aic-clear" title="Clear">' + esc(__("Clear")) + '</button> '
      + '<button class="aic-x" title="Close">×</button></span></div>'
      + '<div class="aic-log"></div>'
      + '<form class="aic-form"><textarea rows="2" dir="auto"></textarea><button type="submit">' + esc(__("Send")) + "</button></form>"
      + '<div class="aic-note">' + esc(__("AI suggestions only. You approve every change.")) + "</div>";
    document.body.append(fab, panel);
    const log = panel.querySelector(".aic-log");
    const form = panel.querySelector("form");
    const ta = form.querySelector("textarea");
    ta.placeholder = "Ask about openings or applicants… / اسأل عن الوظائف والمتقدمين";

    function add(role, text, actions) {
      const el = document.createElement("div");
      el.className = "aic-msg aic-" + role; el.dir = "auto";
      if (role === "bot") el.innerHTML = md(text); else el.textContent = text;
      (actions || []).forEach((a) => {
        const b = document.createElement("button");
        b.className = "aic-approve"; b.textContent = __("Approve") + ": " + a.label;
        b.onclick = () => approve(a, b);
        el.appendChild(b);
      });
      log.appendChild(el); log.scrollTop = log.scrollHeight;
      return el;
    }
    function greet() {
      add("bot", __("Hi! Try **\"Shortlist the top 3 for the AI Engineer role\"** or open an applicant and ask **\"لخّص هذا المتقدم\"**."));
    }
    history.forEach((m) => add(m.role === "user" ? "user" : "bot", m.content));
    if (!history.length) greet();

    function context() {
      const r = frappe.get_route() || [];
      return { route: r.join("/"), doctype: r[1] || null, docname: r[0] === "Form" ? r[2] : null };
    }
    async function approve(a, b) {
      b.disabled = true; b.textContent = __("Applying…");
      try {
        const res = await frappe.xcall("ai_recruitment.api.approve", { action_id: a.id });
        b.textContent = "✓ " + res.result;
        if (window.cur_frm && cur_frm.doc && !cur_frm.is_dirty()) cur_frm.reload_doc();
      } catch (e) {
        b.disabled = false; b.textContent = __("Failed — tap to retry");
      }
    }
    form.onsubmit = async (ev) => {
      ev.preventDefault();
      const q = ta.value.trim(); if (!q) return;
      ta.value = "";
      history.push({ role: "user", content: q }); add("user", q); save();
      const wait = add("bot", "…");
      try {
        const res = await frappe.xcall("ai_recruitment.api.chat", { messages: history, context: context() });
        wait.remove();
        add("bot", res.reply || __("(no reply)"), res.actions);
        history.push({ role: "assistant", content: res.reply || "" }); save();
      } catch (e) {
        wait.innerHTML = md(__("Sorry, something went wrong. Please try again."));
      }
    };
    ta.addEventListener("keydown", (e) => {
      if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); form.requestSubmit(); }
    });
    fab.onclick = () => { panel.classList.toggle("open"); if (panel.classList.contains("open")) ta.focus(); };
    panel.querySelector(".aic-x").onclick = () => panel.classList.remove("open");
    panel.querySelector(".aic-clear").onclick = () => { history = []; save(); log.innerHTML = ""; greet(); };
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", () => frappe.after_ajax(init));
  else frappe.after_ajax(init);
  $(document).on("app_ready", init);
})();
