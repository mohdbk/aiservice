"""Chat widget injected into Frappe Desk as Client Scripts (no custom Frappe app needed)."""

WIDGET_JS = r'''
(function () {
  if (window.__aiRecruitChat) return;
  if (!frappe.user.has_role(["HR User", "HR Manager", "System Manager"])) return;
  window.__aiRecruitChat = true;

  const API = "%%API%%", TOKEN = "%%TOKEN%%", KEY = "ai_recruit_chat_v1";
  let history = [];
  try { history = JSON.parse(sessionStorage.getItem(KEY) || "[]"); } catch (e) {}
  const save = () => { try { sessionStorage.setItem(KEY, JSON.stringify(history.slice(-30))); } catch (e) {} };

  const style = document.createElement("style");
  style.textContent = `
    .aic-fab{position:fixed;right:20px;bottom:20px;z-index:1030;border:0;border-radius:999px;
      padding:10px 16px;font-weight:600;background:var(--primary,#2490ef);color:#fff;
      box-shadow:0 4px 14px rgba(0,0,0,.18);cursor:pointer}
    .aic-panel{position:fixed;right:20px;bottom:72px;z-index:1031;width:min(390px,calc(100vw - 24px));
      height:min(70vh,620px);display:none;flex-direction:column;background:var(--card-bg,#fff);
      color:var(--text-color,#1f272e);border:1px solid var(--border-color,#d1d8dd);border-radius:12px;
      box-shadow:0 10px 30px rgba(0,0,0,.2);overflow:hidden}
    .aic-panel.open{display:flex}
    @media (max-width:600px){.aic-panel{right:12px;bottom:68px}.aic-fab{right:12px;bottom:12px}}
    .aic-head{display:flex;justify-content:space-between;align-items:center;padding:10px 14px;
      font-weight:600;border-bottom:1px solid var(--border-color,#d1d8dd)}
    .aic-x{border:0;background:none;font-size:20px;line-height:1;color:inherit;cursor:pointer}
    .aic-log{flex:1;overflow-y:auto;padding:12px;display:flex;flex-direction:column;gap:8px}
    .aic-msg{max-width:88%;padding:8px 11px;border-radius:10px;white-space:pre-wrap;word-wrap:break-word;font-size:13px;line-height:1.45}
    .aic-user{align-self:flex-end;background:var(--primary,#2490ef);color:#fff}
    .aic-bot{align-self:flex-start;background:var(--subtle-fg,#f4f5f6)}
    .aic-approve{margin-top:8px;border:1px solid var(--primary,#2490ef);background:transparent;
      color:var(--primary,#2490ef);border-radius:8px;padding:6px 10px;font-size:12px;cursor:pointer;text-align:start}
    .aic-approve:disabled{opacity:.7;cursor:default}
    .aic-form{display:flex;gap:8px;padding:10px;border-top:1px solid var(--border-color,#d1d8dd)}
    .aic-form textarea{flex:1;resize:none;border:1px solid var(--border-color,#d1d8dd);border-radius:8px;
      padding:8px;font-size:13px;background:var(--control-bg,#f4f5f6);color:inherit}
    .aic-form button{border:0;border-radius:8px;padding:0 14px;background:var(--primary,#2490ef);color:#fff;font-weight:600}
    .aic-note{font-size:11px;opacity:.65;padding:0 12px 8px}`;
  document.head.appendChild(style);

  const fab = document.createElement("button");
  fab.className = "aic-fab"; fab.textContent = "Ask AI";
  const panel = document.createElement("div");
  panel.className = "aic-panel";
  panel.innerHTML = '<div class="aic-head"><span>Recruitment Assistant</span><button class="aic-x" title="Close">×</button></div>'
    + '<div class="aic-log"></div>'
    + '<form class="aic-form"><textarea rows="2" dir="auto" placeholder="Ask about openings or applicants… / اسأل عن الوظائف والمتقدمين"></textarea><button type="submit">Send</button></form>'
    + '<div class="aic-note">AI suggestions only. You approve every change.</div>';
  document.body.append(fab, panel);
  const log = panel.querySelector(".aic-log"), form = panel.querySelector("form"), ta = form.querySelector("textarea");

  function add(role, text, actions) {
    const el = document.createElement("div");
    el.className = "aic-msg aic-" + role; el.dir = "auto";
    el.textContent = text;
    (actions || []).forEach(function (a) {
      const b = document.createElement("button");
      b.className = "aic-approve"; b.textContent = "Approve: " + a.label;
      b.onclick = function () { approve(a, b); };
      el.appendChild(document.createElement("br")); el.appendChild(b);
    });
    log.appendChild(el); log.scrollTop = log.scrollHeight;
    return el;
  }
  history.forEach(function (m) { add(m.role === "user" ? "user" : "bot", m.content); });
  if (!history.length) add("bot", "Hi! Try: \"Shortlist the top 3 for the AI Engineer role\" or \"لخّص هذا المتقدم\".");

  function ctx() {
    const r = frappe.get_route() || [];
    return { route: r.join("/"), doctype: r[1] || null, docname: r[0] === "Form" ? r[2] : null };
  }
  async function post(path, body) {
    const r = await fetch(API + path, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Chat-Token": TOKEN },
      body: JSON.stringify(body)
    });
    const j = await r.json().catch(function () { return {}; });
    if (!r.ok) throw new Error(j.error || ("HTTP " + r.status));
    return j;
  }
  async function approve(a, b) {
    b.disabled = true; b.textContent = "Applying…";
    try {
      const j = await post("/chat/approve", { token: a.token, user: frappe.session.user });
      b.textContent = "✓ " + j.result;
      if (typeof cur_frm !== "undefined" && cur_frm && !cur_frm.is_dirty()) cur_frm.reload_doc();
    } catch (e) { b.disabled = false; b.textContent = "Failed: " + e.message + " (tap to retry)"; }
  }
  form.onsubmit = async function (ev) {
    ev.preventDefault();
    const q = ta.value.trim(); if (!q) return;
    ta.value = "";
    history.push({ role: "user", content: q }); add("user", q); save();
    const wait = add("bot", "…");
    try {
      const j = await post("/chat", { messages: history, context: ctx(), user: frappe.session.user });
      wait.remove();
      add("bot", j.reply || "(no reply)", j.actions);
      history.push({ role: "assistant", content: j.reply || "" }); save();
    } catch (e) { wait.textContent = "Error: " + e.message; }
  };
  ta.addEventListener("keydown", function (e) {
    if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); form.requestSubmit(); }
  });
  fab.onclick = function () { panel.classList.toggle("open"); if (panel.classList.contains("open")) ta.focus(); };
  panel.querySelector(".aic-x").onclick = function () { panel.classList.remove("open"); };
})();
'''


def render(api_url: str, token: str) -> str:
    return WIDGET_JS.replace("%%API%%", api_url).replace("%%TOKEN%%", token)
