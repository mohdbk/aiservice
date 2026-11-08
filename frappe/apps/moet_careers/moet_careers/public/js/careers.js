// MoET careers portal: accessibility tools, sign-in/up, apply, application tracking.
// Plain JS, no framework. User data is always inserted with textContent.
(function () {
  "use strict";
  var AR = (window.MC && window.MC.lang) === "ar";
  var T = function (en, ar) { return AR ? ar : en; };
  var $ = function (sel, root) { return (root || document).querySelector(sel); };
  var csrf = ($('meta[name="csrf-token"]') || {}).content || "";

  // ---------- accessibility tools ----------
  var store = {
    get: function (k) { try { return localStorage.getItem(k); } catch (e) { return null; } },
    set: function (k, v) { try { localStorage.setItem(k, v); } catch (e) { /* storage blocked */ } },
  };
  var html = document.documentElement;
  function applyText(size) {
    html.classList.remove("mc-text-sm", "mc-text-lg", "mc-text-xl");
    if (size && size !== "md") html.classList.add("mc-text-" + size);
  }
  function applyContrast(on) {
    html.classList.toggle("mc-contrast", on);
    var b = $("[data-mc-contrast]");
    if (b) b.setAttribute("aria-pressed", on ? "true" : "false");
  }
  applyText(store.get("mc-text"));
  applyContrast(store.get("mc-contrast") === "1");
  document.querySelectorAll("[data-mc-text]").forEach(function (b) {
    b.addEventListener("click", function () {
      var cur = store.get("mc-text") || "md", want = b.getAttribute("data-mc-text");
      var order = ["sm", "md", "lg", "xl"];
      var next = want === "lg" ? order[Math.min(order.indexOf(cur) + 1, 3)] : want;
      store.set("mc-text", next); applyText(next);
    });
  });
  var cb = $("[data-mc-contrast]");
  if (cb) cb.addEventListener("click", function () {
    var on = !html.classList.contains("mc-contrast");
    store.set("mc-contrast", on ? "1" : "0"); applyContrast(on);
  });

  // ---------- helpers ----------
  function serverMessage(json, fallback) {
    try {
      if (json && json._server_messages) {
        var msgs = JSON.parse(json._server_messages);
        var m = JSON.parse(msgs[msgs.length - 1]);
        if (m && m.message) return String(m.message).replace(/<[^>]+>/g, "");
      }
      if (json && typeof json.message === "string" && json.message) return json.message;
    } catch (e) { /* fall through */ }
    return fallback;
  }
  function showError(form, text) {
    var box = $("#form-error", form) || $("#apply-error", form);
    if (!box) return;
    $("p", box).textContent = text;
    box.classList.remove("hidden");
    box.setAttribute("tabindex", "-1");
    box.focus();
  }
  function hideError(form) {
    var box = $("#form-error", form) || $("#apply-error", form);
    if (box) box.classList.add("hidden");
  }
  function busy(form, on) {
    form.querySelectorAll("button, input, select, textarea").forEach(function (el) {
      if (el.type === "submit" || el.tagName === "BUTTON") el.disabled = on;
    });
    var b = $("#apply-busy", form);
    if (b) b.classList.toggle("hidden", !on);
  }
  function call(method, opts) {
    opts = opts || {};
    var headers = { "Accept": "application/json" };
    if (csrf) headers["X-Frappe-CSRF-Token"] = csrf;
    var init = { method: opts.method || "POST", headers: headers, credentials: "same-origin" };
    if (opts.form) init.body = opts.form;
    else if (opts.json) { headers["Content-Type"] = "application/json"; init.body = JSON.stringify(opts.json); }
    var url = "/api/method/" + method + (opts.query ? "?" + new URLSearchParams(opts.query) : "");
    return fetch(url, init).then(function (r) {
      return r.json().catch(function () { return {}; }).then(function (j) { return { ok: r.ok, status: r.status, json: j }; });
    });
  }
  function login(email, password) {
    return call("login", { json: { usr: email, pwd: password } });
  }

  // ---------- sign out ----------
  var lo = $("[data-mc-logout]");
  if (lo) lo.addEventListener("click", function () {
    call("logout", { method: "GET" }).finally(function () { window.location.href = "/careers"; });
  });

  // ---------- sign in ----------
  var lf = $("#login-form");
  if (lf) lf.addEventListener("submit", function (e) {
    e.preventDefault(); hideError(lf);
    var email = $("#usr", lf).value.trim(), pwd = $("#pwd", lf).value;
    if (!email || !pwd) return showError(lf, T("Enter your email and password.", "أدخل بريدك الإلكتروني وكلمة المرور."));
    busy(lf, true);
    login(email, pwd).then(function (r) {
      if (r.ok) { window.location.href = lf.getAttribute("data-redirect") || "/careers"; return; }
      busy(lf, false);
      showError(lf, r.status === 429
        ? T("Too many attempts. Please try again later.", "محاولات كثيرة. يرجى المحاولة لاحقا.")
        : T("Incorrect email or password.", "البريد الإلكتروني أو كلمة المرور غير صحيحة."));
    }).catch(function () { busy(lf, false); showError(lf, T("Connection problem. Please try again.", "مشكلة في الاتصال. يرجى المحاولة مرة أخرى.")); });
  });

  // ---------- sign up ----------
  var sf = $("#signup-form");
  if (sf) sf.addEventListener("submit", function (e) {
    e.preventDefault(); hideError(sf);
    var name = $("#full_name", sf).value.trim(), email = $("#email", sf).value.trim(), pwd = $("#password", sf).value;
    if (!name || !email || pwd.length < 8) return showError(sf, T("Fill in all fields. The password needs at least 8 characters.", "املأ جميع الحقول. يجب أن تتكون كلمة المرور من 8 أحرف على الأقل."));
    busy(sf, true);
    call("moet_careers.api.signup", { json: { full_name: name, email: email, password: pwd }, query: { lang: AR ? "ar" : "en" } })
      .then(function (r) {
        if (!r.ok) { busy(sf, false); return showError(sf, serverMessage(r.json, T("Could not create the account.", "تعذر إنشاء الحساب."))); }
        return login(email, pwd).then(function (l) {
          window.location.href = l.ok ? (sf.getAttribute("data-redirect") || "/careers") : "/careers/login";
        });
      }).catch(function () { busy(sf, false); showError(sf, T("Connection problem. Please try again.", "مشكلة في الاتصال. يرجى المحاولة مرة أخرى.")); });
  });

  // ---------- apply ----------
  var af = $("#apply-form");
  if (af) af.addEventListener("submit", function (e) {
    e.preventDefault(); hideError(af);
    var file = $("#cv", af).files[0], maxMb = parseInt(af.getAttribute("data-max-mb") || "5", 10);
    var phone = $("#phone", af).value.trim();
    if (!phone) return showError(af, T("Please enter your mobile number.", "يرجى إدخال رقم هاتفك المتحرك."));
    if (!file) return showError(af, T("Please attach your CV.", "يرجى إرفاق سيرتك الذاتية."));
    if (!/\.(pdf|docx|jpe?g|png)$/i.test(file.name)) return showError(af, T("Upload your CV as PDF, Word (.docx), JPG or PNG.", "ارفع سيرتك الذاتية بصيغة PDF أو Word (.docx) أو JPG أو PNG."));
    if (file.size > maxMb * 1024 * 1024) return showError(af, T("The CV must be smaller than " + maxMb + " MB.", "يجب أن يكون حجم السيرة الذاتية أقل من " + maxMb + " ميجابايت."));
    if (!$("#consent", af).checked) return showError(af, T("Please confirm that your information is accurate.", "يرجى الإقرار بصحة المعلومات."));
    var fd = new FormData();
    fd.append("job", af.getAttribute("data-job"));
    fd.append("phone", phone);
    fd.append("country", $("#country", af).value);
    fd.append("cover_note", $("#cover_note", af).value);
    fd.append("consent", "1");
    fd.append("cv", file, file.name);
    busy(af, true);
    call("moet_careers.api.apply", { form: fd, query: { lang: AR ? "ar" : "en" } }).then(function (r) {
      if (r.ok && r.json.message && r.json.message.application) {
        window.location.href = "/careers/applications/" + encodeURIComponent(r.json.message.application) + "?new=1";
        return;
      }
      busy(af, false);
      showError(af, r.status === 429
        ? T("Too many attempts. Please try again later.", "محاولات كثيرة. يرجى المحاولة لاحقا.")
        : serverMessage(r.json, T("Your application could not be submitted.", "تعذر إرسال طلبك.")));
    }).catch(function () { busy(af, false); showError(af, T("Connection problem. Please try again.", "مشكلة في الاتصال. يرجى المحاولة مرة أخرى.")); });
  });

  // ---------- application: what we read from the CV ----------
  var cr = $("#cv-reading");
  if (cr) {
    var app = cr.getAttribute("data-application"), tries = 0;
    var show = function (state) {
      ["reading", "unreadable", "profile"].forEach(function (s) {
        var el = $('[data-state="' + s + '"]', cr);
        if (el) el.classList.toggle("hidden", s !== state);
      });
    };
    var li = function (txt) { var el = document.createElement("li"); el.textContent = txt; return el; };
    var render = function (p) {
      var none = T("Not found in your CV", "لم يرد في سيرتك الذاتية");
      var years = $('[data-field="years"]', cr);
      years.textContent = p.years ? (p.years + " " + T("years", "سنة")) : none;
      var work = $('[data-field="work"]', cr); work.textContent = "";
      (p.work || []).forEach(function (w) {
        var when = w.start ? " (" + w.start + " " + T("to", "إلى") + " " + (w.end || T("present", "الآن")) + ")" : "";
        work.appendChild(li([w.position, w.employer].filter(Boolean).join(T(", ", "، ")) + when));
      });
      if (!work.children.length) work.appendChild(li(none));
      var edu = $('[data-field="education"]', cr); edu.textContent = "";
      (p.education || []).forEach(function (x) { edu.appendChild(li([x.degree, x.institution, x.end].filter(Boolean).join(T(", ", "، ")))); });
      if (!edu.children.length) edu.appendChild(li(none));
      var sk = $('[data-field="skills"]', cr); sk.textContent = "";
      (p.skills || []).forEach(function (s) { var b = document.createElement("span"); b.className = "aegov-badge badge-sm"; b.textContent = s; sk.appendChild(b); });
      if (!sk.children.length) sk.textContent = none;
      $('[data-field="languages"]', cr).textContent = (p.languages || []).join(T(", ", "، ")) || none;
      $('[data-field="certificates"]', cr).textContent = (p.certificates || []).join(T(", ", "، ")) || none;
      show("profile");
    };
    var poll = function () {
      call("moet_careers.api.application_profile", { method: "GET", query: { application: app, lang: AR ? "ar" : "en" } })
        .then(function (r) {
          var m = r.json && r.json.message;
          if (!r.ok || !m) return;
          if (m.profile) return render(m.profile);
          if (m.unreadable) return show("unreadable");
          if (m.reading && tries++ < 45) setTimeout(poll, 4000);
          else if (!m.reading) show("unreadable");
        }).catch(function () { if (tries++ < 45) setTimeout(poll, 6000); });
    };
    poll();

    var cf = $("#correction-form");
    if (cf) cf.addEventListener("submit", function (e) {
      e.preventDefault();
      var note = $("#note", cf).value.trim();
      if (note.length < 3) return;
      call("moet_careers.api.add_correction", { json: { application: app, note: note }, query: { lang: AR ? "ar" : "en" } })
        .then(function (r) {
          if (r.ok) { $("#note", cf).value = ""; $("[data-sent]", cf).classList.remove("hidden"); }
        });
    });
  }
})();
